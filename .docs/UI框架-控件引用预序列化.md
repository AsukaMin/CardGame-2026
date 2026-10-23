# UI 框架：控件引用预序列化

> 控件绑定唯一标准方案。机制：控件按约定命名 → 编辑器扫描把组件引用序列化进预制体 → 运行时按 key 取用。绑定全部在编辑期完成，运行时零 Find、零 GetComponent；重名在扫描时自动改名消解。实现见 `Unity/Assets/Scripts/UI/UITable.cs`（引用表载体）与 `Unity/Assets/Editor/UITableEditor.cs`（扫描工具）。

## 一、载体：UITable 组件

预序列化引用表存放在 **UITable** 组件上（不绑在 UIWindow 上）：

- **窗口预制体**：根节点挂 UIWindow 子类时 RequireComponent 自动带上 UITable；窗口代码用 `Table` 属性或 Get* 快捷转发取用。
- **脱离窗口单独用**：列表项、小部件等预制体根节点直接挂 UITable，同样右键命名 + 扫描；代码里 `GetComponent<UITable>()` 后按 key 取用。
- **嵌套引用表**：一张表内可以引用另一张表（`$_table_key_$` 节点），每张表有独立的 key 空间。

## 二、命名约定

预制体里给需要被代码访问的节点按固定模式命名。选中节点后在 Hierarchy 右键 `GameObject/UITable/改名为 …` 一键改名（自动按当前名字生成 key，同级重名自动加 `_1`/`_2` 后缀）：

| 节点名 | 收进引用表的类型 | 代码取用 |
|---|---|---|
| `&_image_key_&` | Image | `GetImage("key")` |
| `&_button_key_&` | Button | `GetButton("key")` |
| `&_text_key_&` | TMP_Text | `GetText("key")` |
| `&_gameobject_key_&` | GameObject（不取组件） | `GetGameObject("key")` |
| `$_node_key_$` | 嵌套子窗口（UIWindow 组件） | `GetWindow("key")` |
| `$_table_key_$` | 嵌套引用表（UITable 组件） | `GetTable("key")` |

key 规则：字母/数字/下划线（其他字符在右键改名时归一化为 `_`）；**单张 UITable 内唯一**，跨类型也不能重名（Image 和 Button 共用一个 key 空间）；不同 UITable 互不影响，嵌套表里可以复用外层的 key。`$` 节点是扫描的边界——扫到它就停，不会深入它的子树，子窗口/嵌套表的控件由它自己的 UITable 扫描负责。

## 三、工作流

1. 建窗口预制体：`Resources/UI/` 下新建预制体，**名字 = 窗口类名**（UIManager 的加载约定），根节点挂窗口子类脚本（UITable 会自动带上）。非窗口预制体（列表项、小部件）根节点手动挂一个 UITable 即可。
2. 摆控件，用右键菜单给要访问的节点按上表命名。
3. 选中要扫描的根节点（窗口根或任意 UITable 节点），在该节点的 **UITable 组件** Inspector 底部点 **"扫描子节点"**（仅编辑态可用）：递归收集约定命名的控件，组件引用随预制体序列化保存。嵌套的 `$_table_$` / `$_node_$` 是边界，里面的控件要选中那个节点、对它自己的 UITable 再扫一次。
4. 保存预制体。之后代码里按 key 取用。

要点：

- 扫描是**整体替换**：每次扫描用当前子树重新生成六张引用表。加控件、改名、删控件后重扫一次即可。
- 忘了扫描或 key 打错：`Get*` 返回 null，不会抛异常；窗口代码里对关键控件判空可以早发现问题。
- 组件丢失降级：若某条目序列化的组件引用失效但 GameObject 还在，运行时自动降级进 GameObject 字典——`GetGameObject("key")` 仍可取到该节点。
- 命名可以后期再改：改名后重扫，旧 key 失效、新 key 生效；引用代码同步改即可。

## 四、代码用法

```csharp
public class ShopWindow : UIWindow
{
    public override void OnCreate(UIParam p)
    {
        // 按钮注册放 OnCreate：窗口缓存复用，只需注册一次
        GetButton("buy").onClick.AddListener(OnBuyClicked);
    }

    void OnBuyClicked() { /* ... */ }

    void RefreshCoins(int coins)
    {
        GetText("coins").text = coins.ToString();
        GetImage("coinsBar").fillAmount = coins / 100f;
    }

    void RefreshItem(int slot)
    {
        // 嵌套引用表：成块复用的小 UI（列表项等），里层 key 空间独立
        UITable item = GetTable("item" + slot);
        item.GetText("count").text = "3";
    }
}
```

窗口上的取用 API（`GetImage / GetButton / GetText / GetWindow / GetTable / GetGameObject`）都是对 `Table` 的一行转发；实现都在 UITable 上。脱离窗口的场景先拿到 UITable 组件再调同名 API：

```csharp
UITable table = go.GetComponent<UITable>();
table.GetButton("buy").onClick.AddListener(OnBuyClicked);
```

字典在首次 `Get*` 时懒构建（列表 → 字典搬运一次），之后全是 O(1) 查表；`BuildDictionaries()` 公开可重入，一般无需手调。

引用表只管"引用在哪"，控件的事件订阅（按下/悬浮/拖拽等）配合 UIEventListener 使用——`GetGameObject("key").transform.GetListener().PointDown += ...`，见《UI框架-控件事件监听UIEventListener》。

## 五、注意事项

- **TMP**：文本条目收的是 TMP_Text。首次在场景里使用 TMP 控件时，Unity 会弹窗要求导入 TMP Essential Resources，点导入即可。
- **编辑器脚本位置**：`UITableEditor.cs` 必须待在 `Assets/Editor/` 目录（Editor 程序集），挪走会编译报错（引用 UnityEditor）。
- **条目的 Inspector 绘制设计**：类型化条目的 `comp` 字段一物两用——既是序列化数据，也是点击 ping 定位节点的入口；GameObject 条目没有组件可展示，其 `go` 只是组件丢失时的降级备份（不是主数据，不该手填），故默认隐藏。为补齐同等的定位体验，由 `UITableRefEntryDrawer`（PropertyDrawer）把 GameObject 条目画成"key + go 对象槽"两栏——只作用于 `RefEntry` 基类条目，`RefEntry<T>` 类型化条目仍走默认绘制。
- **子类自定义字段**：窗口子类仍可声明少量私有 `[SerializeField]` 字段拖引用，机制上不冲突；但统一走引用表可以让"看名字就知道哪些控件被代码用了"，新窗口建议全走引用表。
- **`$` 子窗口节点**：`GetWindow("key")` 拿到的是该节点上的 UIWindow 组件实例（嵌套复用的小 UI 块），与 UIManager 管理的窗口生命周期无关，只当普通组件引用用。
- **`$` 嵌套表节点**：`$_table_key_$` 节点要手动挂 UITable 组件（扫描时缺组件会警告，运行期只能 `GetGameObject` 降级取到）；挂好后选中它自己扫描里面的控件。
- **窗口与 UITable**：UIWindow 的引用表一定在同节点的 UITable 上（RequireComponent 强制）；Inspector 里查看/扫描引用表也都在 UITable 组件下。
