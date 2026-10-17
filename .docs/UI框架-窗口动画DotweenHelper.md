# UI 框架：窗口动画（DotweenHelper）

> 接续《单机卡牌游戏-简化版UI框架设计》。结论：动画**不进框架**——纯 Helper 手动调用，UIManager/UIWindow 一行不动。OnShow 里弹开、关闭按钮里"先动画再 Hide"、OnHide 里 KillWindowTweens 兜底。本文定 API、调用约定与 DOTween 安装步骤。

## 一、定位：为什么是手动调用

- 动画是表现细节，不同窗口迟早有各自的花样（全屏窗想淡入、提示窗只想 fade）。统一塞进 UIManager 会把它拖成动画状态机，违背"管理器只管显隐和栈"的初衷。
- 手动的代价：每个弹窗 3 行模板代码；`Back()` / 全屏顶旧这类**不走窗口自己关闭按钮**的路径没有退出动画（已知限制，接受——瞬关不穿帮，只是不华丽）。
- 框架唯一的既有配合点：UIWindow.OnHide 的注释本来就写着"退订事件、**停动画**"——停动画的实现就是 `KillWindowTweens`。

## 二、DOTween 安装（已完成，随仓库走）

DOTween 没有 UPM/OpenUPM 官方包，别折腾 manifest.json。免费版 1.3.045 已解压进
`Unity/Assets/Plugins/Demigiant/DOTween/`（核心 DLL + 模块源码），Unity 打开自动导入即可用。
DotweenHelper 用到的核心 + UI 模块默认开启（`DOTweenModuleUI.cs` 的守卫是 `#if !DOTWEEN_NOUI`），**不需要跑 Setup**。

- 可选：Tools → Demigiant → DOTween Utility Panel → Setup DOTween… —— 想启用 TMP 等附加模块的 DOTween 快捷方式时才需要（DotweenHelper 没用到）。
- 以后从 Asset Store 导入 Pro 版：官方包就落在同一路径 `Plugins/Demigiant`，直接覆盖合并，无冲突。

## 三、标准用法（每个弹窗 3 行）

```csharp
public class MyPopup : UIWindow
{
    protected override void OnShow(UIParam p) => DotweenHelper.PopOpen(this);   // ① 弹开+遮罩

    protected override void OnHide() => DotweenHelper.KillWindowTweens(this);   // ② 停动画兜底

    void OnCloseClick()   // 关闭按钮（OnCreate 里 GetButton("close").onClick.AddListener 注册）
        => DotweenHelper.PopClose(this, () => UIManager.I.Hide(nameof(MyPopup)));   // ③ 先动画再关
}
```

- **时序**：`UIManager.Hide` 保持瞬时不动；动画顺序是"播收起动画 → 播完才 Hide"。
- **遮罩点击关窗**：`DotweenHelper.PopOpen(this, onDimClick: () => UIManager.I.Back())`。
- **不要遮罩**（比如弹窗本身半屏且想留着下层可点）：`PopOpen(this, withDim: false)`。

## 四、API 一览

| 方法 | 用途 |
|---|---|
| `PopOpen(win, withDim, onDimClick, duration, dimAlpha)` | 弹开：缩放 0.85→1（OutBack）+ CanvasGroup 淡入（自动补挂）；可选垫全屏黑遮罩；返回 Sequence 供续接 |
| `PopClose(win, onComplete, duration)` | 收起：缩放+淡出 → 复位窗口状态 → 回调（在里面调 `UIManager.Hide`） |
| `KillWindowTweens(win)` | OnHide 兜底：杀光窗口 tween、瞬间移除遮罩、复位状态 |
| `NumberRoll(label, from, to, duration, format)` | TMP 数字滚动（HP/金币），同 label 重复调用自动顶掉上次 |
| `FloatText(parent, anchoredPos, content, color, ...)` | 纯代码建临时 TMP 文本，上浮+淡出自毁，零预制体依赖 |

## 五、行为约定（这几条别破）

1. **全部 unscaled time**（`SetUpdate(true)`）：`timeScale=0` 的暂停菜单里动画照播。
2. **动画期间窗口不可交互**（`CanvasGroup.interactable=false`）：防误点、防连点关闭按钮。
3. **窗口缓存复用 → 动画结束必须复位**：PopClose 播完会把 scale/alpha/interactable 还原成干净状态（helper 已做，别在 onComplete 里再手改 scale）。
4. **遮罩 = 模态**：挡住对下层窗口的点击；多层弹窗各有遮罩、叠加加深，属正常表现。
5. **竞态安全**：PopOpen 开头自动杀旧动画（关闭中重开安全）；窗口被 Back()/全屏顶旧直接 Hide 时，OnHide → KillWindowTweens 兜底，不留悬空遮罩。
6. **tween 归属**：窗口相关 tween 一律以窗口对象为 Target，所以 `DOTween.Kill(win)` 一句杀干净——窗口里自己另外播的 tween 别用窗口当 Target，会被误杀。

## 六、已知限制

- `Back()` / 全屏顶旧关窗**没有**退出动画（手动模式的代价，见第一节）。
- 飘字是 Canvas 锚点坐标系（UI 空间）；战斗世界空间飘字（头顶血条那种）留给表现层设计时扩展。
- 全屏窗建议不用 PopOpen（满屏缩放观感差）；需要时 `withDim:false`，或等后续加 Fade 变体。

## 七、后续可扩展（本次未做）

- 按钮点击反馈：统一按压缩放 punch，做成绑定方法。
- 通用小工具：Fade / Shake（拒绝抖动）/ Punch（强调）/ 循环呼吸（红点提示）。
- 世界空间飘字 + 飘字对象池（战斗演出量大时再上）。
