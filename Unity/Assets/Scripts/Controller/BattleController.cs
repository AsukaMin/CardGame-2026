using System.IO;
using Battle;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 战斗装配件（场景挂一次，M3 引导脚本，Controller 文件夹首位成员；未来 M4 流程控制器同进此文件夹）。
/// 读卡表 → 组演示牌组 → 开战斗会话 → 开战斗窗。时序铁律：先 UIManager.Show("BattleWindow")
/// （OnShow 订阅事件 + 全量刷）、后 session.Start()（晚订阅就收不到 BattleStarted）。
/// 重开一局：Hide 战斗窗（OnHide 成对退订，防跨局串台）→ 新会话 → 再 Show。
/// 卡表路径与 Editor/BattleSmokeMenu.CardsJsonPath 同款（Assets→Unity→仓库根→GameData/cards.json）；
/// 路径纪律（目录结构文档§四）：本类是过渡的读表点，Loader/ConfigDatabase 落地时把 File.ReadAllText
/// 那段收编进去（业务代码不碰路径）。M3 只保证编辑器播放模式；打包期的 Config/Mods 查找归 Loader 规划。
/// </summary>
public sealed class BattleController : MonoBehaviour
{
    public static BattleController I { get; private set; }

    [Tooltip("首局随机种子；每重开一局 +1（同种子同对局——确定性回归锚定用）")]
    [SerializeField] int battleSeed = 20261019;

    int restartCount;

    /// <summary>当前对局会话；窗口（UI/Windows/BattleWindow）从这里拿引用调命令/读值。</summary>
    public BattleSession Session { get; private set; }

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    void Start() => BuildBattle();

    /// <summary>重开一局（R 键 / 未来 ResultWindow 的"再来一局"按钮调这里）。</summary>
    public void Restart()
    {
        restartCount++;
        if (UIManager.I != null) UIManager.I.Hide("BattleWindow"); // OnHide 成对退订
        BuildBattle();
    }

    void BuildBattle()
    {
        if (UIManager.I == null)
        {
            Debug.LogError("[BattleController] 场景缺 UIManager 组件——建一个空物体挂 UIManager（层字段全留空自动建根）");
            return;
        }

        string path = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "GameData", "cards.json");
        if (!File.Exists(path))
        {
            Debug.LogError($"[BattleController] 找不到卡表 {path}——M3 只保证编辑器播放模式（仓库根 GameData）");
            return;
        }

        CardCatalog catalog = CardCatalog.Load(File.ReadAllText(path));
        Session = new BattleSession(DemoBattleData.CreateSetup(catalog, battleSeed + restartCount));

        UIManager.I.Show("BattleWindow"); // 先开窗（订阅 + 全量刷）……
        Session.Start();                  // ……后开战（BattleStarted 等事实事件 UI 才收得到）
    }

    void Update()
    {
        // M3 临时：R 键重开一局（ResultWindow 做好后由"再来一局"按钮替换本入口）
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            Restart();
    }
}
