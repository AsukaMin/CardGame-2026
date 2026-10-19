using System.Collections.Generic;

namespace Battle
{
    /// <summary>一场战斗的构造参数（一切数据构造注入，无 IO；M2 起由 Loader 导表产出）。</summary>
    public sealed class BattleSetup
    {
        public int PlayerHp = 30;
        public int EnemyHp = 30;

        /// <summary>玩家初始能量（召唤/出牌共用；回复规则见 EnergyRule）。</summary>
        public int InitialEnergy = 2;

        /// <summary>能量回复策略；null = 每我方回合开始 +2（FlatPerTurnRule(2) 默认）。</summary>
        public IEnergyRule EnergyRule;

        /// <summary>玩家牌库（索引 0 = 顶，先抽）。</summary>
        public List<BattleCard> Deck = new List<BattleCard>();

        /// <summary>起手手牌数。</summary>
        public int StartingHandSize = 3;

        /// <summary>敌方召唤列表（敌方回合依序召唤一只，耗尽则跳过；敌方召唤免费）。</summary>
        public List<MonsterCardDef> EnemySummons = new List<MonsterCardDef>();

        /// <summary>随机种子（敌方攻击目标选择等；同种子同操作=同终局）。</summary>
        public int RandomSeed;
    }
}
