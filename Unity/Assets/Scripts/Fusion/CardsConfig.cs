using System;
using Battle;

namespace Fusion
{
    /// <summary>卡槽四型：身材/入场/常驻/离场。</summary>
    public enum SlotType { Body, OnSummon, Continuous, OnLeave }

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

    /// <summary>
    /// 部位卡配置：身材槽部位卡纯身材；效果槽部位卡携带行为（槽型即声明，v2.1 定案）。
    /// 行为载荷类型与语义见《技能系统设计》——常驻槽 = Auras（静态光环）+ Triggers（事件触发行为）并存。
    /// </summary>
    [Serializable]
    public class PartCardConfig
    {
        public int Id;
        public string Name = "";
        /// <summary>槽型声明，须与卡套槽位匹配才能装入。</summary>
        public SlotType SlotType = SlotType.Body;
        /// <summary>写死的费用（槽内卡不充能，召唤时一次付清）。</summary>
        public int Cost;
        /// <summary>攻击底数（仅身材槽合法，装槽时校验）。</summary>
        public int Attack;
        /// <summary>生命底数（仅身材槽合法）。</summary>
        public int MaxHp;
        /// <summary>行为效果组（仅 OnSummon/OnLeave 槽合法；触发时机由槽型路由：召唤时/死亡时）。</summary>
        public EffectGroupDef[] Effects;
        /// <summary>事件触发行为（仅常驻槽合法——"受伤反弹/攻击后成长"类）。</summary>
        public TriggerDef[] Triggers;
        /// <summary>静态光环（仅常驻槽合法——挂上时施加的持续效果定义）。</summary>
        public GameplayEffectDef[] Auras;
        /// <summary>随召唤授予宿主单位的标签名（如 "Keyword.Taunt"）。</summary>
        public string[] GrantTags;
        /// <summary>消耗标记：单位死亡时随包销毁，战后不回收（宿主战斗核心消费）。</summary>
        public bool Consumable;
    }
}
