# 单机卡牌游戏：简化版 UI 框架设计

> 整理自 2026-10-13 讨论，接续《单机卡牌对战游戏-板块划分与离线补丁方案》。结论：单机卡牌不上 ET 框架，用"纯 C# 融合核心 + 简化版 UI 管理 + JSON 配置"三件套；本文是其中 UI 层的完整设计。

## 一、为什么不用 ET

| ET 解决的问题 | 单机卡牌 |
|---|---|
| 网络同步/多人对战 | 不需要，本地打 AI |
| 服务器逻辑 | 没有 |
| 代码热更（ILRuntime/双程序集） | 已定走 JSON 数据补丁，代码不热更 |
| 登录/网关/心跳 | 没有 |

成本却是实打实的：ILRuntime 绑定过期、跨域委托、重导 dll、框架版本升级迁移。个人项目所有复杂度自己付，零收益。

**ET 的思想照搬**：组件化组织、窗口管理模式、配置表驱动——搬思想不搬框架。

## 二、简化对照表

| ET 的机制 | 简化版 |
|---|---|
| UIComponent + UIWindow 双组件 | 一个 UIWindow 基类 |
| UIObjs 名字字典建引用 | **弃用**；控件绑定改走"命名扫描 + 序列化引用表"（见下文） |
| ShowAsync 协程加载 | 同步加载 |
| EventSystem 事件派发 | C# event |
| OnCreate/OnShow/OnHide 生命周期、OpenType 传参、隐藏不销毁缓存复用 | **照搬保留** |

关键简化：**窗口类直接做成 MonoBehaviour**。预制体根节点挂窗口脚本；控件引用在编辑期扫描序列化（见下文"控件引用"），运行时零查找。

## 三、核心代码

### UIWindow.cs —— 窗口基类

每个窗口 = 一个预制体 + 这个类的一个子类。

```csharp
public abstract class UIWindow : MonoBehaviour
{
    public bool Opened { get; internal set; }

    public virtual void OnCreate(UIParam param) { }  // 只走一次：初始化列表、按钮注册
    public virtual void OnShow(UIParam param) { }    // 每次打开：吃参数、刷新显示
    public virtual void OnHide() { }                 // 隐藏：退订事件、停动画

    // 控件引用：预序列化引用表（见下节），GetButton("key") / GetText("key") 按取用
}
```

### 控件引用：命名扫描 + 预序列化

窗口里的控件引用不在运行时按名字查找，一律编辑期解决：

1. 预制体里给控件按约定命名（Hierarchy 选中节点右键 `GameObject/UIWindow/改名为 …` 一键完成）：

   | 节点名 | 含义 | 代码取用 |
   |---|---|---|
   | `&_image_key_&` | Image | `GetImage("key")` |
   | `&_button_key_&` | Button | `GetButton("key")` |
   | `&_text_key_&` | TMP_Text | `GetText("key")` |
   | `&_gameobject_key_&` | 任意节点 | `GetGameObject("key")` |
   | `$_node_key_$` | 嵌套子窗口（扫描到此为止，不深入） | `GetWindow("key")` |

2. 选中窗口根节点，Inspector 点"扫描子节点"（Editor/UIWindowEditor）：递归收集所有约定命名的控件，把组件引用随预制体序列化保存；重名 key 自动加后缀改名消解。
3. 运行时只把序列化列表搬进字典，按 key 取用，零 Find/零 GetComponent。

完整工作流与注意事项见《UI框架-控件引用预序列化》。

### UIManager.cs —— 三层管理器

```csharp
public class UIManager : MonoBehaviour
{
    public static UIManager I { get; private set; }

    [SerializeField] Transform fullscreenLayer;   // 全屏窗：互斥，开新顶旧
    [SerializeField] Transform popupLayer;        // 弹窗：可叠加，按栈回退
    [SerializeField] Transform tipLayer;          // 提示：过路即走

    readonly Dictionary<string, UIWindow> cache = new Dictionary<string, UIWindow>();
    readonly List<string> popupStack = new List<string>();

    void Awake() { I = this; }

    public void Show(string winName, UIParam param = null)
    {
        if (!cache.TryGetValue(winName, out UIWindow win))
        {
            GameObject prefab = Resources.Load<GameObject>("UI/" + winName); // 约定：预制体名=类名
            win = Instantiate(prefab, LayerOf(winName)).GetComponent<UIWindow>();
            cache[winName] = win;
        }

        win.gameObject.SetActive(true);
        if (!win.Opened)
        {
            win.Opened = true;
            win.OnCreate(param);
        }
        win.OnShow(param);
        // 全屏窗顶掉同层旧的；弹窗压栈（几行 if/列表操作）
    }

    public void Hide(string winName)
    {
        if (cache.TryGetValue(winName, out UIWindow win))
        {
            win.OnHide();
            win.gameObject.SetActive(false);   // 缓存复用，不销毁——ET 同款策略
        }
    }

    public void Back()
    {
        if (popupStack.Count > 0) Hide(popupStack[popupStack.Count - 1]);
    }
}
```

### UIParam.cs —— OpenType + 参数包

```csharp
public class UIParam
{
    public string OpenType;   // "Edit" / "View" / "Shop"……
    readonly Dictionary<string, object> map = new Dictionary<string, object>();

    public void Set(string key, object value) { map[key] = value; }
    public T Get<T>(string key) { object v; map.TryGetValue(key, out v); return (T)v; }
}
```

用法：

```csharp
UIParam p = new UIParam();
p.OpenType = "Edit";
p.Set("DeckId", 1001);
UIManager.I.Show("DeckWindow", p);
```

## 四、跨窗口通信：三条通道

**原则：先分方向，别混用。**

| 方向 | 通道 | 原则 |
|---|---|---|
| 逻辑核心 → UI（"发生了什么"） | C# event 广播 | 事件只报事实，过去时态，不关心谁在听 |
| UI → 逻辑核心（"我要做什么"） | **直接调方法**，不走事件 | 要返回值、要失败原因 |
| UI ↔ UI（窗口间） | 一次性应答用**回调**；全局广播才用事件 | 大多数"窗口间通信"是父子对话 |

### 1. 逻辑核心 → UI：事件广播

（示例用局外货币域 MetaEvents；战斗域事件类待战斗核心重写后照此模式建立。）

```csharp
// MetaEvents.cs —— 局外域唯一对外的"嘴"
public static class MetaEvents
{
    public static event Action<int> OnCoinsChanged;     // (新货币数)
    public static event Action<int> OnCardUnlocked;     // 卡牌Id
}
```

逻辑核心只管发，不知道有没有 UI 在听：

```csharp
void AddCoins(int amount)
{
    coins += amount;
    MetaEvents.OnCoinsChanged?.Invoke(coins);   // ?. 判空：没 UI 也照常跑
}
```

UI 侧**铁律：OnShow 订阅、OnHide 退订，永远成对**；且订阅后立刻按当前状态全量刷新一次（晚开窗会错过历史事件）：

```csharp
public class ShopWindow : UIWindow
{
    void RefreshCoins(int coins) => GetText("coins").text = coins.ToString(); // 控件来自序列化引用表

    public override void OnShow(UIParam p)
    {
        MetaEvents.OnCoinsChanged += RefreshCoins;
        RefreshAll();   // 订阅后立刻全量刷，否则界面是错的
    }

    public override void OnHide()
    {
        MetaEvents.OnCoinsChanged -= RefreshCoins;
    }
}
```

### 2. UI → 逻辑核心：直接调用

```csharp
public void OnBuyClicked(int itemId)
{
    BuyResult r = shop.Buy(itemId);   // 逻辑核心的命令方法：要返回值、要失败原因
    if (!r.Success)
        ShowTip(r.Error);
}
```

事件没有返回值；用事件做命令等于把一次函数调用拆成"发事件→收回执→再订阅回执"，凭空造出异步和时序问题。**事件宣布事实，方法调用下命令。**

### 3. UI ↔ UI：先问"是不是父子对话"

一次性选择用**回调参数**，调用方把自己的意图塞进 UIParam，被调窗口零依赖可复用：

```csharp
// CardPickWindow：通用选卡弹窗
public class CardPickWindow : UIWindow
{
    Action<int> onPicked;

    public override void OnShow(UIParam p)
    {
        onPicked = p.Get<Action<int>>("OnPicked");
        RefreshList(p.Get<List<int>>("CandidateIds"));
    }

    public void OnCardClicked(int cardId)
    {
        onPicked?.Invoke(cardId);
        UIManager.I.Hide("CardPickWindow");
    }
}

// DeckWindow：调用方
UIParam p = new UIParam();
p.Set("CandidateIds", ownedCardIds);
p.Set("OnPicked", (Action<int>)(cardId => AddToDeck(cardId)));
UIManager.I.Show("CardPickWindow", p);
```

什么时候用事件做 UI↔UI：**多个窗口要同时知道同一件事**（如货币变化，主界面/商店/卡包页都要刷）。

## 五、两个要防的坑

1. **过期订阅**：窗口缓存不销毁时配对订阅/退订不会出事；若改成"关闭即 Destroy"，忘退订的窗口就是悬空引用，下一发事件就 NRE。OnShow 加几行 +=，OnHide 就得有几行 -=。
2. **静态事件跨场残留**：回主菜单重开一局时，上一局订阅者没退干净会收到新一局的事件。第 1 条做到即自动成立；再保险可在重开局时发 `OnRunReset` 之类的重置事件让漏网窗口自刷。

## 六、规模化整理

事件多了按业务域拆文件，**不换机制**：

```csharp
BattleEvents.cs   // 战斗域（重写后重建）：掉血、抽牌、回合
MetaEvents.cs     // 局外：货币、卡牌解锁、卡组保存
UiEvents.cs       // UI 自己的事：弹窗关闭、红点变化
```

**不要**写字符串/Type 当 key 的反射式 EventBus——事件名拼错编译器不报错，那是 ET 为大型项目付的灵活性代价。三五个静态类、编译期全检查，单机卡牌绰绰有余。

> 【2026-10-17 修订，2026-10-18 更新】跨模块的**全局事件系统**（GameEventBus：**struct 事件 + 泛型订阅**，类型即键，编译期全检）与各域静态事件**共存不迁移**；原首个使用场景（表现层→融合核心的演出时序回调）已随战斗重构移除，待战斗重写后重建。禁令精神不变（禁字符串/反射键），形态与纪律见《单机卡牌游戏-事件系统设计》。

## 七、收拢

> **逻辑核心对 UI 只有事件（说事实），UI 对逻辑核心只有方法（下命令），窗口对窗口先考虑回调（父子对话），事件只留给"多个窗口共同关心的一件事"。**

整套 UI 框架两三百行：一个管理器 + 一个窗口基类 + 一个参数包 + 几个静态事件类。没有反射、没有运行时查找、没有异步链，复杂度全部留给卡牌玩法本身。
