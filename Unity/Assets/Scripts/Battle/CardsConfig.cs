using System;

namespace Battle
{
    /// <summary>消耗公式：召唤费 = max(0, round(Σ部位卡费 × Multiplier) + Flat)。例：75% → (0.75, 0)；-2 总耗 → (1.0, -2)。</summary>
    [Serializable]
    public class CostFormula
    {
        public double Multiplier = 1.0;
        public int Flat;
    }

    /// <summary>怪兽卡套配置：布局即身份，本体无任何攻血数值（攻血来自身材槽部位卡，系数与容量来自卡套）。</summary>
    [Serializable]
    public class MonsterCardConfig
    {
        public int Id;
        public string Name = "";
        /// <summary>槽位布局，如 [Body, OnSummon, OnLeave]。</summary>
        public SlotType[] Slots = Array.Empty<SlotType>();
        /// <summary>消耗公式（对槽内部位卡费用总和的修正）。</summary>
        public CostFormula Cost = new CostFormula();
        /// <summary>身材百分比修正（对身材槽部位卡攻血总和的乘算，130% 豪华型等）。</summary>
        public double StatMultiplier = 1.0;
    }

    /// <summary>部位卡配置：身材槽部位卡纯身材；效果槽部位卡携带行为（槽型即声明，v2.1 定案）。</summary>
    [Serializable]
    public class PartCardConfig
    {
        public int Id;
        public string Name = "";
        /// <summary>槽型声明，须与卡套槽位匹配才能装入。</summary>
        public SlotType SlotType = SlotType.Body;
        /// <summary>写死的费用（槽内卡不充能，召唤时一次付清）。</summary>
        public int Cost;
        /// <summary>攻击底数（仅身材槽有意义）。</summary>
        public int Attack;
        /// <summary>生命底数（仅身材槽有意义）。</summary>
        public int MaxHp;
        /// <summary>行为效果（仅效果槽有意义；触发时机由槽型决定）。</summary>
        public EffectDef[] Effects;
        /// <summary>随召唤赋予的关键词（如嘲讽甲壳）。</summary>
        public Keywords GrantKeywords = Keywords.None;
        /// <summary>消耗标记：单位死亡时随包销毁，战后不回收。</summary>
        public bool Consumable;
    }

    /// <summary>效果卡配置：只能从手牌打出、按档位面值充能、永不吃折扣；与卡套无任何装配关系（v2.1 定案）。</summary>
    [Serializable]
    public class EffectCardConfig
    {
        public int Id;
        public string Name = "";
        /// <summary>充能档位，按费用升序：低投入=低配效果，高投入=完全体。</summary>
        public EffectTier[] Tiers = Array.Empty<EffectTier>();
    }

    /// <summary>充能档位：面值费用 + 对应效果。</summary>
    [Serializable]
    public class EffectTier
    {
        public int Cost;
        /// <summary>显示用标签，如"低配"/"完全体"。</summary>
        public string Label = "";
        public EffectDef[] Effects;
    }

    /// <summary>敌方主战者配置（敌方不走行动点——PvE 规则不对称，文档明示）。</summary>
    [Serializable]
    public class EnemyConfig
    {
        public int Id;
        public string Name = "敌方主战者";
        public int MaxHp = 20;
        /// <summary>意图攻击力区间 [Min, Max]（含两端）。</summary>
        public int AttackDamageMin = 3;
        public int AttackDamageMax = 6;
        /// <summary>施法意图候选（每项一组效果，目标选择相对敌方阵营）。</summary>
        public EffectDef[][] CastPool = Array.Empty<EffectDef[]>();
    }

    /// <summary>战斗调参：数值不写死在玩法里，全部可配置（导表管线的落点）。</summary>
    [Serializable]
    public class BattleTuning
    {
        public int PlayerMaxHp = 30;
        /// <summary>开局行动点（全局保留不随回合清零；增长靠回复策略）。</summary>
        public int InitialActionPoints = 3;
        public int StartingHandSize = 4;
        public int CardsDrawnPerTurn = 2;
        /// <summary>每方场上单位上限；0=不设上限（文档：席位限制已否决）。</summary>
        public int FieldUnitCap = 0;
    }
}
