using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 战斗入参：核心通过构造函数接收已加载好的数据，此后纯内存运算——这是它能脱离 Unity 单独测试的原因（目录结构文档的边界纪律）。
    /// 卡套必须已在整备阶段装好部位卡并保证合法（≥1 身材槽部位卡）。
    /// </summary>
    public class BattleSetup
    {
        public BattleTuning Tuning = new BattleTuning();

        /// <summary>牌库组成（效果卡配置，可重复）。</summary>
        public List<EffectCardConfig> DeckCards = new List<EffectCardConfig>();

        /// <summary>固定抽牌顺序（配置 Id 序列，测试复现用）；null=按种子洗牌。</summary>
        public List<int> FixedDeckOrder;

        /// <summary>玩家怪兽卡套名单（战前整备好，每张每场限召一次）。</summary>
        public List<MonsterSleeve> PlayerRoster = new List<MonsterSleeve>();

        public EnemyConfig Enemy = new EnemyConfig();

        /// <summary>敌方召唤意图用的卡套池（同样须已整备合法）。</summary>
        public List<MonsterSleeve> EnemySummonPool = new List<MonsterSleeve>();

        /// <summary>随机种子（复现 bug 的锚点）。</summary>
        public int Seed = 20261017;

        /// <summary>表现挂起模式：true 时 PlayAfterAnimation 效果组挂起，等待 EntranceAnimDone 全局事件放行（默认 false=立即结算，无 UI/headless 场景零影响）。见《战斗核心框架设计》表现挂起节。</summary>
        public bool DeferPresentationEvents;

        /// <summary>行动点回复策略（可空=本战斗无回复）。</summary>
        public IRecoveryPolicy Recovery;
    }
}
