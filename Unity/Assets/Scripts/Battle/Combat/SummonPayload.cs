using System;

namespace Battle
{
    /// <summary>
    /// 组装怪兽的战斗投影（M5·T6）：整备期装好的卡套（MonsterSleeve）经宿主侧投影层编译成本载体，
    /// 战斗核心只认本类型——纯 Battle.Data 既有词汇，不另造效果字段（M2 决定事项执行要点 1）。
    /// 依赖方向硬约束：Battle 不引用 Fusion（asmdef 单向 Fusion→Battle），双引用只发生在投影层。
    /// 数值面 = 投影时算好的召唤费与攻血（源=MonsterSleeve.SummonCost/BaseStats，现成）；
    /// 行为面 = 四路载荷，接线形状见《融合核心框架设计》§五（OnSummon→Execute / Continuous→Register+施加 / OnLeave→死亡时 Execute）。
    /// </summary>
    [Serializable]
    public sealed class SummonPayload
    {
        public string Name = "";
        /// <summary>公式召唤费（Σ部位费×乘数+定值，投影时固化）。</summary>
        public int Cost;
        public int Attack;
        public int MaxHp;
        /// <summary>入场效果组（OnSummon 槽 Effects 聚合；召唤时执行一次，Source=新单位）。</summary>
        public EffectGroupDef[] OnSummonEffects;
        /// <summary>离场效果组（OnLeave 槽 Effects 聚合；死亡时执行一次，Source=亡单位）。</summary>
        public EffectGroupDef[] OnLeaveEffects;
        /// <summary>常驻触发器（Continuous 槽 Triggers 聚合；随单位注册，离场注销）。</summary>
        public TriggerDef[] ContinuousTriggers;
        /// <summary>静态光环（Continuous 槽 Auras 聚合；施加给我方全体单位，Source=本单位——死亡精准撤除）。
        /// v1 定案：光环目标=我方全体；按光环单独定目标属数据 schema 扩展（M5-API缺口清单立档）。</summary>
        public GameplayEffectDef[] Auras;
        /// <summary>随召唤授予宿主单位的标签名（如 "Keyword.Taunt"）。</summary>
        public string[] GrantTags;
        /// <summary>消耗标记：死亡随包销毁、战后不回收（回收清单 M6；M5 仅数据携带）。</summary>
        public bool Consumable;
    }
}
