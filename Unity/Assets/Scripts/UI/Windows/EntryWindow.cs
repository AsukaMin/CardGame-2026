/// <summary>
/// 入口窗（M4）：开始游戏 → BattleController.StartBattle。由 UIManager.startWindow（场景字段）
/// 在启动时自动打开；不订阅事件、不吃参数，按钮注册放 OnCreate（窗口缓存复用只走一次）。
/// 层=Fullscreen——全屏互斥：开 BattleWindow 时 UIManager 自动顶掉本窗，回到入口时再拉起。
/// 控件 key 与 Resources/UI/EntryWindow.prefab 根节点 UITable 的扫描结果一一对应。
/// </summary>
public sealed class EntryWindow : UIWindow
{
    public override UIWindowLayer Layer => UIWindowLayer.Fullscreen;

    public override void OnCreate(UIParam param)
    {
        GetButton("startButton")?.onClick.AddListener(OnStartClicked);
    }

    void OnStartClicked() => BattleController.Instance.StartBattle();
}
