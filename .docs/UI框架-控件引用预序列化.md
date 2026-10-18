# UI 框架：控件引用预序列化

> 控件绑定唯一标准方案。机制：控件按约定命名 → 编辑器扫描把组件引用序列化进预制体 → 运行时按 key 取用。绑定全部在编辑期完成，运行时零 Find、零 GetComponent；重名在扫描时自动改名消解。实现见 `Unity/Assets/Scripts/UI/UIWindow.cs`（引用表）与 `Unity/Assets/Editor/UIWindowEditor.cs`（扫描工具）。

## 一、命名约定

预制体里给需要被代码访问的节点按固定模式命名。选中节点后在 Hierarchy 右键 `GameObject/UIWindow/改名为 …` 一键改名（自动按当前名字生成 key，同级重名自动加 `_1`/`_2` 后缀）：

| 节点名 | 收进引用表的类型 | 代码取用 |
|---|---|---|
| `&_image_key_&` | Image | `GetImage("key")` |
| `&_button_key_&` | Button | `GetButton("key")` |
| `&_text_key_&` | TMP_Text | `GetText("key")` |
| `&_gameobject_key_&` | GameObject（不取组件） | `GetGameObject("key")` |
| `$_node_key_$` | 嵌套子窗口（UIWindow 组件） | `GetWindow("key")` |

key 规则：字母/数字/下划线（其他字符在右键改名时归一化为 `_`）；**全窗口内唯一**，跨类型也不能重名（Image 和 Button 共用一个 key 空间）；`$` 节点是扫描的边界——扫到它就停，不会深入它的子树，子窗口的控件由它自己的扫描负责。

## 二、工作流

1. 建窗口预制体：`Resources/UI/` 下新建预制体，**名字 = 窗口类名**（UIManager 的加载约定），根节点挂窗口子类脚本。
2. 摆控件，用右键菜单给要访问的节点按上表命名。
3. 选中窗口根节点，Inspector 底部点 **"扫描子节点"**（仅编辑态可用）：递归收集约定命名的控件，组件引用随预制体序列化保存。
4. 保存预制体。之后代码里按 key 取用。

要点：

- 扫描是**整体替换**：每次扫描用当前子树重新生成五张引用表。加控件、改名、删控件后重扫一次即可。
- 忘了扫描或 key 打错：`Get*` 返回 null，不会抛异常；窗口代码里对关键控件判空可以早发现问题。
- 组件丢失降级：若某条目序列化的组件引用失效但 GameObject 还在，运行时自动降级进 GameObject 字典——`GetGameObject("key")` 仍可取到该节点。
- 命名可以后期再改：改名后重扫，旧 key 失效、新 key 生效；引用代码同步改即可。

## 三、代码用法

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
}
```

取用 API（都在 UIWindow 基类上，找不到返回 null）：`GetImage / GetButton / GetText / GetWindow / GetGameObject`。

字典在首次 `Get*` 时懒构建（列表 → 字典搬运一次），之后全是 O(1) 查表；`BuildDictionaries()` 公开可重入，一般无需手调。

## 四、注意事项

- **TMP**：文本条目收的是 TMP_Text。首次在场景里使用 TMP 控件时，Unity 会弹窗要求导入 TMP Essential Resources，点导入即可。
- **编辑器脚本位置**：`UIWindowEditor.cs` 必须待在 `Assets/Editor/` 目录（Editor 程序集），挪走会编译报错（引用 UnityEditor）。
- **子类自定义字段**：窗口子类仍可声明少量私有 `[SerializeField]` 字段拖引用，机制上不冲突；但统一走引用表可以让"看名字就知道哪些控件被代码用了"，新窗口建议全走引用表。
- **`$` 子窗口节点**：`GetWindow("key")` 拿到的是该节点上的 UIWindow 组件实例（嵌套复用的小 UI 块），与 UIManager 管理的窗口生命周期无关，只当普通组件引用用。
