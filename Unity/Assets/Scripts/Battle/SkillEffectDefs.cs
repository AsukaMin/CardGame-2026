using System;

namespace Battle
{
    /// <summary>伤害来源类别：Effect=效果伤害 / Combat=战斗伤害（意图伤害由宿主归类）。</summary>
    public enum DamageSourceKind { None = 0, Effect, Combat }

    /// <summary>Instant 操作基类：解释执行归 OpSpec（工厂分发）。</summary>
    [Serializable]
    public abstract class EffectOp { }

    /// <summary>伤害：经结算管线（Preview 聚合改写→改 Base→Applied 事件）。</summary>
    [Serializable]
    public sealed class DamageOp : EffectOp
    {
        public ScopedValue Amount = ScopedValue.Const(1);
        public DamageSourceKind Kind = DamageSourceKind.Effect;
    }

    /// <summary>治疗：经结算管线（上限夹紧）。</summary>
    [Serializable]
    public sealed class HealOp : EffectOp
    {
        public ScopedValue Amount = ScopedValue.Const(1);
    }

    /// <summary>
    /// 属性修改：Duration=Permanent→直改 Base（永久）；否则→引擎包装为隐式持续效果携带该修饰（到期回落、可驱散）。
    /// </summary>
    [Serializable]
    public sealed class ModifyAttributeOp : EffectOp
    {
        public AttributeId Attribute = AttributeId.Attack;
        public ModOp Op = ModOp.Add;
        public ScopedValue Magnitude = ScopedValue.Const(1);
        public EffectDurationKind Duration = EffectDurationKind.Permanent;
        /// <summary>Duration==Rounds 时的持续轮数。</summary>
        public int Rounds = 1;
    }

    /// <summary>授予标签：统一经持续效果载体（grantedTags）——可驱散、可到期。</summary>
    [Serializable]
    public sealed class GrantTagOp : EffectOp
    {
        public string Tag = "";
        public EffectDurationKind Duration = EffectDurationKind.Permanent;
        public int Rounds = 1;
    }

    /// <summary>按标签驱散：移除目标身上 grantedTags 命中的持续效果（引用计数保护，不直接摘标签）。</summary>
    [Serializable]
    public sealed class RemoveTagOp : EffectOp
    {
        public string Tag = "";
    }

    /// <summary>施加持续效果（Buff/毒/光环的唯一入口）。</summary>
    [Serializable]
    public sealed class ApplyEffectOp : EffectOp
    {
        public GameplayEffectDef Effect;
    }

    /// <summary>条件分支：按 ConditionTarget 指定的对象求值，命中走 Then，否则 Else。可嵌套。</summary>
    [Serializable]
    public sealed class ConditionalOp : EffectOp
    {
        public ConditionDef Condition;
        public ConditionTargetKind ConditionTarget = ConditionTargetKind.Current;
        public EffectOp[] Then = Array.Empty<EffectOp>();
        public EffectOp[] Else = Array.Empty<EffectOp>();
    }

    /// <summary>
    /// 改写待结算数值（减伤/易伤/免疫=归零）：Amount' = Amount×Scale + Delta，下限 0。
    /// 仅 Preview 触发上下文合法，其余上下文=配置错误抛异常。
    /// </summary>
    [Serializable]
    public sealed class ModifyEventMagnitudeOp : EffectOp
    {
        public double Delta;
        public double Scale = 1.0;
    }

    /// <summary>持续时长：Instant（立即改写 Base 与永久标签，不入容器）/ ThisPhase（相位结束清除）/ Rounds(N) / Permanent（可驱散）。</summary>
    public enum EffectDurationKind { Instant, ThisPhase, Rounds, Permanent }

    /// <summary>堆叠归属：BySource（同源同效果叠层）/ ByTarget（同类效果不分来源叠层）。</summary>
    public enum StackAggregation { BySource, ByTarget }

    /// <summary>持续效果的属性修饰定义（挂靠时经 ScopedValue 求值固化）。</summary>
    [Serializable]
    public sealed class AttributeModifierDef
    {
        public AttributeId Attribute = AttributeId.Attack;
        public ModOp Op = ModOp.Add;
        public ScopedValue Magnitude = ScopedValue.Const(1);
    }

    /// <summary>
    /// 持续效果定义——Buff/毒/光环的统一载体（GAS GameplayEffect）。
    /// 光环=挂在别人身上的持续效果：宿主在来源实体离场时 RemoveEffectsFromSource 撤销。
    /// </summary>
    [Serializable]
    public sealed class GameplayEffectDef
    {
        public string Name = "";
        /// <summary>身份标签：驱散匹配 + 堆叠归属参考。</summary>
        public string[] AssetTags;
        /// <summary>挂上时授予目标（引用计数），移除时摘除。</summary>
        public string[] GrantedTags;
        /// <summary>目标必备标签（缺则不施加）。</summary>
        public string[] ApplicationRequiredTags;
        /// <summary>目标带则免疫（不施加）。</summary>
        public string[] ApplicationImmunityTags;
        public EffectDurationKind Duration = EffectDurationKind.Rounds;
        public int Rounds = 1;
        /// <summary>每回合周期执行（宿主回合时点驱动：engine.TickRound）。</summary>
        public bool PeriodicEachRound;
        /// <summary>周期效果组：执行上下文 Source=效果宿主（Self 即宿主——"毒对自己跳伤"的写法）。</summary>
        public EffectGroupDef PeriodicEffects;
        public AttributeModifierDef[] Modifiers;
        public StackAggregation StackBy = StackAggregation.ByTarget;
        /// <summary>堆叠上限（0=无限）。</summary>
        public int StackLimit;
        /// <summary>叠层时刷新剩余轮数。</summary>
        public bool StackRefreshDuration = true;
        /// <summary>到期行为：true=掉一层并刷新，false=整效果移除。</summary>
        public bool ExpirePerStack;
    }
}
