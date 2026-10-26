using TMPro;

/// <summary>
/// 终局弹窗（M4）：胜负文案 + 再来一局 / 回到入口。层=Popup（UIWindow 默认）。
/// 不订阅任何事件——胜负经 UIParam 带入（BattleWindow.On(BattleEnded) 里 param.Set("PlayerWon", …)），
/// OnShow 只读参数刷文案，无退订负担；按钮注册放 OnCreate（窗口缓存复用只走一次，不重复挂监听）。
/// 控件 key 与 Resources/UI/ResultWindow.prefab 根节点 UITable 的扫描结果一一对应。
/// </summary>
public sealed class ResultWindow : UIWindow
{
    public override void OnCreate(UIParam param)
    {
        GetButton("restartButton")?.onClick.AddListener(OnRestartClicked);
        GetButton("backEntryButton")?.onClick.AddListener(OnBackEntryClicked);
    }

    public override void OnShow(UIParam param)
    {
        bool won = param != null && param.TryGet("PlayerWon", out bool playerWon) && playerWon;
        TMP_Text result = GetText("resultText");
        if (result != null) result.text = won ? "胜利！" : "失败…";
    }

    // 再来一局：先收起自己，再重开（M4 任务清单 A5——Restart 内部 Hide BattleWindow → 新会话 → Show）
    void OnRestartClicked()
    {
        UIManager.Instance.Hide("ResultWindow");
        BattleController.Instance.Restart();
    }

    void OnBackEntryClicked() => BattleController.Instance.BackToEntry();
}
