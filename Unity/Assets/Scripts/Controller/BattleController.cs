using System.IO;
using Battle;
using Fusion;
using Tool;
using UnityEngine;

/// <summary>
/// 流程控制器（场景挂一次；M3 引导脚本在 M4 升级而来）：入口 →（整备）→ 开战 → 结算 → 回到入口的循环。
/// 单例经 Tool.MonoSingleton：不写 Awake，访问 Instance 时场景没有会自动建物体（battleSeed 取默认值）。
/// 开战时序铁律：先 UIManager.Instance.Show("BattleWindow")（OnShow 订阅事件 + 全量刷）、
/// 后 session.Start()（晚订阅就收不到 BattleStarted）。
/// 入口窗由 UIManager.startWindow（场景字段）自动打开，本类不再自动建局；
/// 开战=StartBattle（EntryWindow 开始按钮；本次 Play 首次先弹整备窗——T6.4 定案）、
/// 重开=Restart（ResultWindow 再来一局，不回整备）、回入口=BackToEntry。
/// 卡表路径与 Editor/BattleSmokeMenu.CardsJsonPath 同款（Assets→Unity→仓库根→GameData/…）；
/// 路径纪律（目录结构文档§四）：本类是过渡的读表点，Loader/ConfigDatabase 落地时把 File.ReadAllText
/// 那段收编进去（业务代码不碰路径）。M5 仍只保证编辑器播放模式；打包期的 Config/Mods 查找归 Loader 规划。
/// </summary>
public sealed class BattleController : MonoSingleton<BattleController>
{
    [Tooltip("首局随机种子；每重开一局 +1（同种子同对局——确定性回归锚定用）")]
    [SerializeField] int battleSeed = 20261019;
    [Tooltip("使用的牌组 id（GameData/decks.json）——2=入门牌组（M5 量产卡），1=演示牌组（M1 锚定序）")]
    [SerializeField] int deckId = 2;

    int restartCount;
    bool prepShown;                 // 本次 Play 会话只弹一次整备（v1 交互定案，策划案§一）
    FusionCatalog fusion;           // 融合数据懒加载（parts/sleeves——整备窗消费）

    /// <summary>当前对局会话；窗口（UI/Windows/BattleWindow）从这里拿引用调命令/读值。回到入口后为 null。</summary>
    public BattleSession Session { get; private set; }

    /// <summary>整备产物（M5·T6）：整备窗确认时写入、跳过置 null；开战时投影成 BattleCard 插牌库顶。重开沿用。</summary>
    public MonsterSleeve CurrentSleeve { get; set; }

    /// <summary>融合数据（parts/sleeves，懒加载）；读表失败返回 null（整备窗自动跳过，不拦流程）。</summary>
    public FusionCatalog Fusion
    {
        get
        {
            if (fusion != null) return fusion;
            string gameData = Path.Combine(
                Directory.GetParent(Application.dataPath).Parent.FullName, "GameData");
            string partsPath = Path.Combine(gameData, "parts.json");
            string sleevesPath = Path.Combine(gameData, "sleeves.json");
            if (!File.Exists(partsPath) || !File.Exists(sleevesPath))
            {
                Debug.LogError($"[BattleController] 融合数据缺失：{partsPath} / {sleevesPath}——整备跳过（M5 仍只保证编辑器播放模式）");
                return null;
            }
            fusion = FusionCatalog.Load(File.ReadAllText(partsPath), File.ReadAllText(sleevesPath));
            return fusion;
        }
    }

    /// <summary>开始一局（EntryWindow 开始按钮调）。本次 Play 首次先弹整备窗（确认/跳过后 BeginBattle）。</summary>
    public void StartBattle()
    {
        if (!prepShown)
        {
            prepShown = true;
            UIManager.Instance.Show("PrepWindow"); // Fullscreen：顶掉入口窗；出口在整备窗（确认/跳过 → BeginBattle）
            return;
        }
        BeginBattle();
    }

    /// <summary>真正建局开战（整备窗出口直调；Restart 复用）。
    /// 组牌/敌方阵容自 M5·T3 起数据化：cards/decks/enemies 三表齐读，敌人按局数顺序轮换；
    /// 整备产物（M5·T6）投影成组装怪兽卡插牌库顶（起手必带）。</summary>
    public void BeginBattle()
    {
        string gameData = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "GameData");
        string cardsPath = Path.Combine(gameData, "cards.json");
        if (!File.Exists(cardsPath))
        {
            Debug.LogError($"[BattleController] 找不到卡表 {cardsPath}——M5 仍只保证编辑器播放模式（仓库根 GameData）");
            return;
        }

        CardCatalog catalog = CardCatalog.Load(File.ReadAllText(cardsPath));
        DeckCatalog decks = DeckCatalog.Load(File.ReadAllText(Path.Combine(gameData, "decks.json")), catalog);
        EnemyCatalog enemies = EnemyCatalog.Load(File.ReadAllText(Path.Combine(gameData, "enemies.json")), catalog);
        EnemyDef enemy = enemies.All[restartCount % enemies.All.Count]; // 敌人按局数顺序轮换（确定性；选关/地图归 M6）
        DeckDef deck = decks.Get(deckId);
        BattleSetup setup = DemoBattleData.CreateSetup(catalog, deck, enemy, battleSeed + restartCount);
        if (CurrentSleeve != null)
            setup.Deck.Insert(0, SleeveProjector.Compile(CurrentSleeve)); // 卡套投影：插牌库顶，起手必带
        Debug.Log($"[BattleController] 对局：牌组「{deck.Name}」" +
                  $"{(CurrentSleeve != null ? $"+组装怪兽「{CurrentSleeve.Config.Name}」" : "")} vs 敌人「{enemy.Name}」（{enemy.LeaderHp} HP）");
        Session = new BattleSession(setup);

        // UIManager 是 MonoSingleton：场景忘了挂也不拦——首次访问自动建物体（层根照建），不缺功能性
        UIManager.Instance.Show("BattleWindow"); // 先开窗（订阅 + 全量刷）……
        Session.Start();                         // ……后开战（BattleStarted 等事实事件 UI 才收得到）
    }

    /// <summary>重开一局（ResultWindow"再来一局"按钮调；结算窗会先 Hide 自己再进这里；不回整备，沿用 CurrentSleeve）。</summary>
    public void Restart()
    {
        restartCount++;
        UIManager.Instance.Hide("BattleWindow"); // OnHide 成对退订
        BeginBattle();
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
