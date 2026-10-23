# UI 框架：控件事件监听 UIEventListener

> 控件级输入交互的唯一标准方案。实现见 `Unity/Assets/Scripts/UI/UIEventListener.cs`——EventSystem 全部输入接口到 C# 事件的适配器，挂在 UI 节点上即用，代码订阅、零 Inspector 配置。

## 一、定位与动机

uGUI 的输入回调分散在 EventSystem 的 18 个接口里（`IPointerClickHandler`、`IBeginDragHandler`、`IScrollHandler`……），各有各的签名，逐个实现又啰嗦又污染窗口类；`Button.onClick` 只覆盖"点击"一种交互，按下/抬起/悬浮/拖拽/滚轮/选中/导航统统没有统一的代码出口。

UIEventListener 一个组件把全部接口适配成 17 个 C# 事件，窗口代码按需订阅：

| 事件组 | 事件 |
|---|---|
| 指针 | `PointDown` / `PointUp` / `PointClick` / `PointEnter` / `PointExit`（参数 `PointerEventData`） |
| 拖拽 | `InitializePotentialDrag` / `BeginDrag` / `Drag` / `EndDrag` / `Drop`（参数 `PointerEventData`） |
| 滚轮 | `Scroll`（参数 `PointerEventData`） |
| 选中 | `Select` / `Deselect` / `UpdateSelected`（参数 `BaseEventData`） |
| 导航/提交 | `Move`（参数 `AxisEventData`）、`Submit` / `Cancel`（参数 `BaseEventData`） |

## 二、与现有框架的分层（不冲突）

| 层 | 通道 | 管什么 |
|---|---|---|
| 控件输入 | **UIEventListener** | 单个 UI 节点的指针/拖拽/滚轮/选中交互 |
| 窗口级广播 | UiEvents（OnWindowClosed / OnRedPointChanged） | 跨窗口的全局 UI 事件 |
| 逻辑核心 → UI | C# event（各业务域事件类） | "发生了什么"的事实广播 |
| UI → 逻辑核心 | 直接调方法 | 要返回值、要失败原因的命令 |

预序列化引用表（UITable）管"引用在哪"（数据），UIEventListener 管"交互怎么响应"（行为），二者互补：引用表按 key 取节点，listener 在节点上挂事件。"事件报事实、方法调用下命令"的通信纪律见《单机卡牌游戏-简化版UI框架设计》。

## 三、用法

两个入口，按方便选用（扩展方法糖等价静态入口）：

```csharp
UIEventListener l1 = UIEventListener.GetListener(transform);   // 静态入口（get-or-add：没有就自动补挂）
UIEventListener l2 = transform.GetListener();                  // 扩展调用糖
```

窗口里与引用表配合——按 key 取节点，再挂监听：

```csharp
public class BattleWindow : UIWindow
{
    public override void OnCreate(UIParam p)
    {
        // 卡牌按下/悬浮：没有 Button 也要响应的交互走 listener
        var card = GetGameObject("card1").transform.GetListener();
        card.PointDown += OnCardDown;
        card.PointEnter += e => /* 放大高亮 */;
        card.PointExit  += e => /* 复原 */;

        // 有 Button 的走 onClick（自带过渡态），别用 PointClick 双份订阅
        GetButton("buy").onClick.AddListener(OnBuyClicked);
    }

    void OnCardDown(PointerEventData e) { /* ... */ }
}
```

订阅纪律（框架铁律在控件层的延伸）：

- **一次性注册放 OnCreate**：窗口缓存复用（隐藏不销毁），listener 随节点常驻，OnCreate 里 `+=` 一次终身有效。
- **动态订阅必须成对**：随窗口显隐开关的订阅，OnShow 里 `+=`、OnHide 里同数 `-=`——漏退订会在常驻 listener 上叠加回调，每次打开多触发一次。
- **幂等防重**：`DoesHavePointClick(nameof(OnBuy))` 等按方法名查订阅表，订阅前判断可防重复注册（17 个事件各有一个对应方法）。

## 四、Button 与 listener 的取舍

同节点上 Button.onClick 与 listener 的 PointClick **都会触发**（ExecuteEvents 通知所有实现者），不是二选一的互斥，而是规矩：

- 挂了 Button 的节点：点击用 `onClick`（自带过渡态、Inspector 可配）。
- 非 Button 节点、或点击之外的交互（按下即响应、悬浮预览、拖拽、滚轮）：用 listener。
- 同一个点击动作只订阅一处，别双份响应。

## 五、注意事项

- **raycastTarget**：节点上要有 Graphic（Image/Text 等）且 `Raycast Target` 开着，指针事件才收得到；纯空节点挂 listener 无效。
- **Input System 兼容**：工程是 Input System only（InputSystemUIInputModule），该模块照常派发全部 ExecuteEvents 接口，listener 无需任何适配。
- **拖拽与滚动**：实现拖拽接口的节点在 ScrollRect 里可能抢滚动事件，嵌滚动列表的拖拽交互留意测试。
- **组件无序列化数据**：事件是 C# event（代码订阅），prefab 里不用也无法配置——需要 Inspector 拖配的场合（策划配回调）仍走 Button/UnityEvent。
- **get-or-add**：`GetListener` 首次调用自动 AddComponent，之后复用；listener 是无状态适配器，不进预序列化引用表、不占命名约定。
