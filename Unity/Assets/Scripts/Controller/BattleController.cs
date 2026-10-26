using System.IO;
using Battle;
using Tool;
using UnityEngine;

/// <summary>
/// 流程控制器（场景挂一次；M3 引导脚本在 M4 升级而来）：入口 → 开战 → 结算 → 回到入口的循环。
/// 单例经 Tool.MonoSingleton：不写 Awake，访问 Instance 时场景没有会自动建物体（battleSeed 取默认值）。
/// 开战时序铁律：先 UIManager.Instance.Show("BattleWindow")（OnShow 订阅事件 + 全量刷）、
/// 后 session.Start()（晚订阅就收不到 BattleStarted）。
/// 入口窗由 UIManager.startWindow（场景字段）自动打开，本类不再自动建局；
/// 开战=StartBattle（EntryWindow 开始按钮）、重开=Restart（ResultWindow 再来一局）、回入口=BackToEntry（ResultWindow 回到入口）。
/// 卡表路径与 Editor/BattleSmokeMenu.CardsJsonPath 同款（Assets→Unity→仓库根→GameData/cards.json）；
/// 路径纪律（目录结构文档§四）：本类是过渡的读表点，Loader/ConfigDatabase 落地时把 File.ReadAllText
/// 那段收编进去（业务代码不碰路径）。M4 仍只保证编辑器播放模式；打包期的 Config/Mods 查找归 Loader 规划。
/// </summary>
public sealed class BattleController : MonoSingleton<BattleController>
{
    [Tooltip("首局随机种子；每重开一局 +1（同种子同对局——确定性回归锚定用）")]
    [SerializeField] int battleSeed = 20261019;

    int restartCount;

    /// <summary>当前对局会话；窗口（UI/Windows/BattleWindow）从这里拿引用调命令/读值。回到入口后为 null。</summary>
    public BattleSession Session { get; private set; }

    /// <summary>开始一局（EntryWindow 开始按钮调；M3 的自动建局 BuildBattle 公开化而来）。</summary>
    public void StartBattle()
    {
        string path = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "GameData", "cards.json");
        if (!File.Exists(path))
        {
            Debug.LogError($"[BattleController] 找不到卡表 {path}——M4 仍只保证编辑器播放模式（仓库根 GameData）");
            return;
        }

        CardCatalog catalog = CardCatalog.Load(File.ReadAllText(path));
        Session = new BattleSession(DemoBattleData.CreateSetup(catalog, battleSeed + restartCount));

        // UIManager 是 MonoSingleton：场景忘了挂也不拦——首次访问自动建物体（层根照建），不缺功能性
        UIManager.Instance.Show("BattleWindow"); // 先开窗（订阅 + 全量刷）……
        Session.Start();                         // ……后开战（BattleStarted 等事实事件 UI 才收得到）
    }

    /// <summary>重开一局（ResultWindow"再来一局"按钮调；结算窗会先 Hide 自己再进这里）。</summary>
    public void Restart()
    {
        restartCount++;
        UIManager.Instance.Hide("BattleWindow"); // OnHide 成对退订
        StartBattle();
    }

    /// <summary>回到入口（ResultWindow"回到入口"按钮调）：收两扇窗、清会话、拉起入口窗。</summary>
    public void BackToEntry()
    {
        UIManager.Instance.Hide("ResultWindow");
        UIManager.Instance.Hide("BattleWindow");
        Session = null; // BattleWindow 未开局占位（"准备中…" / HP"—"）扛得住 null 会话；下次从入口进走全新 Show → Start
        UIManager.Instance.Show("EntryWindow");
    }
}
