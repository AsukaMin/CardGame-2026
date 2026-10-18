using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 执行上下文：一次执行中所有节点共享的黑板（demo SpecExecutionContext 的回合制简化版）。
    /// 逐目标执行时切换 CurrentTarget（for-each 的等价物）。
    /// </summary>
    public sealed class SkillContext
    {
        /// <summary>效果来源（效果卡=施法领袖；部位卡=宿主单位；触发器=触发器宿主，世界级触发器可空）。</summary>
        public SkillEntity Source;
        /// <summary>命令传入的主目标（可空）。</summary>
        public SkillEntity MainTarget;
        /// <summary>当前被作用实体（ExecuteGroup 逐目标切换）。</summary>
        public SkillEntity CurrentTarget;
        /// <summary>触发上下文的事件（非触发上下文为 default）。</summary>
        public GameplayEvent Event;
        /// <summary>SetByCaller 载荷（宿主/上游传值）。</summary>
        public Dictionary<string, double> SetByCaller;
        /// <summary>最近结算结果（DamageResult/HealResult），供后续 op 与宿主引用。</summary>
        public object LastResult;

        public double GetSetByCaller(string key) =>
            SetByCaller != null && SetByCaller.TryGetValue(key, out double v) ? v : 0;

        public static SkillContext Of(SkillEntity source) => new SkillContext { Source = source };
    }

    /// <summary>伤害结算结果（管线返回值，也写入 ctx.LastResult）。</summary>
    public readonly struct DamageResult
    {
        public readonly SkillEntity Target;
        /// <summary>实际造成的数值（落地前后差值）。</summary>
        public readonly double Amount;
        public DamageResult(SkillEntity target, double amount) { Target = target; Amount = amount; }
    }

    /// <summary>治疗结算结果。</summary>
    public readonly struct HealResult
    {
        public readonly SkillEntity Target;
        public readonly double Amount;
        public HealResult(SkillEntity target, double amount) { Target = target; Amount = amount; }
    }

    // ==================== OpSpec 族（Instant 操作运行时） ====================

    /// <summary>Op 运行时基类：工厂创建，Execute 经 engine 触达世界。</summary>
    public abstract class OpSpec
    {
        public EffectOp Data;
        public abstract void Execute(SkillEngine engine, SkillContext ctx);
    }

    public sealed class DamageOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            DamageOp op = (DamageOp)Data;
            SkillEntity target = ctx.CurrentTarget;
            if (target == null) return;
            double amount = engine.Evaluate(op.Amount, ctx);
            ctx.LastResult = engine.ResolveDamage(ctx.Source, target, amount, op.Kind);
        }
    }

    public sealed class HealOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            HealOp op = (HealOp)Data;
            SkillEntity target = ctx.CurrentTarget;
            if (target == null) return;
            double amount = engine.Evaluate(op.Amount, ctx);
            ctx.LastResult = engine.ResolveHeal(ctx.Source, target, amount);
        }
    }

    public sealed class ModifyAttributeOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            ModifyAttributeOp op = (ModifyAttributeOp)Data;
            SkillEntity target = ctx.CurrentTarget;
            if (target == null) return;
            double magnitude = engine.Evaluate(op.Magnitude, ctx);

            if (op.Duration == EffectDurationKind.Permanent)
            {
                // 永久修改：直改 Base（Add/Multiply/Override 三档语义）
                double b = target.Attributes.Base(op.Attribute);
                switch (op.Op)
                {
                    case ModOp.Add: target.Attributes.SetBase(op.Attribute, b + magnitude); break;
                    case ModOp.Multiply: target.Attributes.SetBase(op.Attribute, b * magnitude); break;
                    case ModOp.Override: target.Attributes.SetBase(op.Attribute, magnitude); break;
                }
                return;
            }
            // 带时长：包装为隐式持续效果（到期回落、可驱散）
            engine.ApplyTransientModifier(target, ctx.Source,
                op.Attribute, op.Op, magnitude, op.Duration, op.Rounds);
        }
    }

    public sealed class GrantTagOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            GrantTagOp op = (GrantTagOp)Data;
            SkillEntity target = ctx.CurrentTarget;
            if (target == null || string.IsNullOrEmpty(op.Tag)) return;
            SkillTag tag = TagRegistry.Resolve(op.Tag);
            engine.ApplyTransientTag(target, ctx.Source, tag, op.Duration, op.Rounds);
        }
    }

    public sealed class RemoveTagOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            RemoveTagOp op = (RemoveTagOp)Data;
            SkillEntity target = ctx.CurrentTarget;
            if (target == null || string.IsNullOrEmpty(op.Tag)) return;
            engine.DispelByTag(target, TagRegistry.Resolve(op.Tag));
        }
    }

    public sealed class ApplyEffectOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            ApplyEffectOp op = (ApplyEffectOp)Data;
            SkillEntity target = ctx.CurrentTarget;
            if (target == null || op.Effect == null) return;
            target.Effects.ApplyOrStack(engine, op.Effect, ctx.Source, ctx);
        }
    }

    public sealed class ConditionalOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            ConditionalOp op = (ConditionalOp)Data;
            if (op.Condition == null) { engine.ExecuteOps(op.Then, ctx); return; }

            SkillEntity subject = ResolveSubject(op.ConditionTarget, ctx);
            if (subject == null) { engine.ExecuteOps(op.Else, ctx); return; }

            ConditionSpec spec = SpecFactory.CreateCondition(op.Condition);
            engine.ExecuteOps(spec.Eval(ctx, subject) ? op.Then : op.Else, ctx);
        }

        public static SkillEntity ResolveSubject(ConditionTargetKind kind, SkillContext ctx) =>
            kind == ConditionTargetKind.Source ? ctx.Source
            : kind == ConditionTargetKind.MainTarget ? ctx.MainTarget
            : ctx.CurrentTarget;
    }

    public sealed class ModifyEventMagnitudeOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            ModifyEventMagnitudeOp op = (ModifyEventMagnitudeOp)Data;
            engine.ModifyPendingMagnitude(op.Delta, op.Scale);
        }
    }

    // ==================== ConditionSpec 族 ====================

    /// <summary>条件运行时基类：对指定对象求值。</summary>
    public abstract class ConditionSpec
    {
        public ConditionDef Data;
        public abstract bool Eval(SkillContext ctx, SkillEntity subject);
    }

    public sealed class AttributeCompareConditionSpec : ConditionSpec
    {
        public override bool Eval(SkillContext ctx, SkillEntity subject)
        {
            if (subject == null) return false;
            AttributeCompareCondition c = (AttributeCompareCondition)Data;
            double left = subject.Attributes.Current(c.Attribute);
            double right = c.ValueType == CompareValueType.PercentOfMaxHp
                ? subject.Attributes.Current(AttributeId.MaxHp) * c.Value / 100.0
                : c.Value;
            switch (c.Op)
            {
                case CompareOperator.Greater: return left > right;
                case CompareOperator.GreaterOrEqual: return left >= right;
                case CompareOperator.Less: return left < right;
                case CompareOperator.LessOrEqual: return left <= right;
                case CompareOperator.Equal: return Math.Abs(left - right) < 1e-9;
                case CompareOperator.NotEqual: return Math.Abs(left - right) >= 1e-9;
                default: return false;
            }
        }
    }

    public sealed class EventValueConditionSpec : ConditionSpec
    {
        public override bool Eval(SkillContext ctx, SkillEntity subject)
        {
            if (!ctx.Event.IsValid) return false;
            EventValueCondition c = (EventValueCondition)Data;
            double m = ctx.Event.Magnitude;
            switch (c.Op)
            {
                case CompareOperator.Greater: return m > c.Value;
                case CompareOperator.GreaterOrEqual: return m >= c.Value;
                case CompareOperator.Less: return m < c.Value;
                case CompareOperator.LessOrEqual: return m <= c.Value;
                case CompareOperator.Equal: return Math.Abs(m - c.Value) < 1e-9;
                case CompareOperator.NotEqual: return Math.Abs(m - c.Value) >= 1e-9;
                default: return false;
            }
        }
    }

    public sealed class HasTagConditionSpec : ConditionSpec
    {
        public override bool Eval(SkillContext ctx, SkillEntity subject)
        {
            if (subject == null) return false;
            HasTagCondition c = (HasTagCondition)Data;
            if (string.IsNullOrEmpty(c.Tag)) return false;
            SkillTag tag = TagRegistry.Resolve(c.Tag);
            return c.Exact ? subject.Tags.HasExact(tag) : subject.Tags.Has(tag);
        }
    }

    // ==================== 工厂 ====================

    /// <summary>类型路由工厂（内置 op/条件；宿主自定义 op 经 engine.RegisterOp 走实例级注册表优先）。</summary>
    public static class SpecFactory
    {
        public static OpSpec CreateOp(EffectOp data)
        {
            switch (data)
            {
                case DamageOp _: return new DamageOpSpec { Data = data };
                case HealOp _: return new HealOpSpec { Data = data };
                case ModifyAttributeOp _: return new ModifyAttributeOpSpec { Data = data };
                case GrantTagOp _: return new GrantTagOpSpec { Data = data };
                case RemoveTagOp _: return new RemoveTagOpSpec { Data = data };
                case ApplyEffectOp _: return new ApplyEffectOpSpec { Data = data };
                case ConditionalOp _: return new ConditionalOpSpec { Data = data };
                case ModifyEventMagnitudeOp _: return new ModifyEventMagnitudeOpSpec { Data = data };
                default: return null; // 宿主自定义 op 或未知类型——engine 决定报错方式
            }
        }

        public static ConditionSpec CreateCondition(ConditionDef data)
        {
            switch (data)
            {
                case AttributeCompareCondition _: return new AttributeCompareConditionSpec { Data = data };
                case EventValueCondition _: return new EventValueConditionSpec { Data = data };
                case HasTagCondition _: return new HasTagConditionSpec { Data = data };
                default: return null;
            }
        }
    }

    // ==================== 持续效果运行时 ====================

    /// <summary>
    /// 持续效果运行时实例：修饰随效果挂/摘（Source=自身，精准撤销）、grantedTags 引用计数、
    /// 堆叠层数推进（标脏重算）、周期与到期由容器在回合时点驱动。
    /// </summary>
    public sealed class ActiveEffectSpec
    {
        public GameplayEffectDef Def;
        public SkillEntity Target;
        public SkillEntity Source;
        public int StackCount = 1;
        public int RemainingRounds;
        public bool Removed;

        readonly List<AttributeModifier> applied = new List<AttributeModifier>();
        readonly List<SkillTag> granted = new List<SkillTag>();

        /// <summary>挂上（前置/免疫校验已由容器完成）。修饰数值在应用时刻求值固化。</summary>
        public void Apply(SkillEngine engine, SkillContext ctx)
        {
            if (Def.Duration == EffectDurationKind.Rounds)
                RemainingRounds = Math.Max(1, Def.Rounds);

            if (Def.Modifiers != null)
            {
                foreach (AttributeModifierDef md in Def.Modifiers)
                {
                    if (md == null) continue;
                    AttributeModifier m = new AttributeModifier
                    {
                        Attribute = md.Attribute,
                        Op = md.Op,
                        Magnitude = engine.Evaluate(md.Magnitude, ctx),
                        Source = this,
                        StackCount = 1,
                    };
                    Target.Attributes.AddModifier(m);
                    applied.Add(m);
                }
            }

            if (Def.GrantedTags != null)
            {
                foreach (string name in Def.GrantedTags)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    SkillTag tag = TagRegistry.Resolve(name);
                    Target.Tags.Add(tag);
                    granted.Add(tag);
                }
            }

            engine.EnqueueEvent(SkillTags.EventEffectApplied, Source, Target, StackCount, DamageSourceKind.None);
        }

        /// <summary>Instant 特例：立即改写 Base 与永久授予标签，不入容器。</summary>
        public void ApplyInstant(SkillEngine engine, SkillContext ctx)
        {
            if (Def.Modifiers != null)
            {
                foreach (AttributeModifierDef md in Def.Modifiers)
                {
                    if (md == null) continue;
                    double v = engine.Evaluate(md.Magnitude, ctx);
                    double b = Target.Attributes.Base(md.Attribute);
                    switch (md.Op)
                    {
                        case ModOp.Add: Target.Attributes.SetBase(md.Attribute, b + v); break;
                        case ModOp.Multiply: Target.Attributes.SetBase(md.Attribute, b * v); break;
                        case ModOp.Override: Target.Attributes.SetBase(md.Attribute, v); break;
                    }
                }
            }
            if (Def.GrantedTags != null)
                foreach (string name in Def.GrantedTags)
                    if (!string.IsNullOrEmpty(name)) Target.Tags.Add(TagRegistry.Resolve(name));
            engine.EnqueueEvent(SkillTags.EventEffectApplied, Source, Target, 1, DamageSourceKind.None);
        }

        /// <summary>叠层（上限拒绝返回 false）；层数立即参与聚合，刷新剩余轮数按策略。</summary>
        public bool AddStack()
        {
            if (Def.StackLimit > 0 && StackCount >= Def.StackLimit) return false;
            StackCount++;
            if (Def.StackRefreshDuration && Def.Duration == EffectDurationKind.Rounds)
                RemainingRounds = Math.Max(1, Def.Rounds);
            for (int i = 0; i < applied.Count; i++)
            {
                applied[i].StackCount = StackCount;
                Target.Attributes.Touch(applied[i].Attribute);
            }
            return true;
        }

        /// <summary>回合时点：周期执行 → 剩余轮数递减 → 到期（掉层或移除）。</summary>
        public void Tick(SkillEngine engine)
        {
            if (Removed) return;

            if (Def.PeriodicEachRound && Def.PeriodicEffects != null)
            {
                // 周期上下文：Source=效果宿主（Self 即宿主——"毒对自己跳伤"的自然写法）
                SkillContext periodicCtx = new SkillContext
                {
                    Source = Target, MainTarget = Target, CurrentTarget = Target,
                };
                engine.ExecuteGroup(Def.PeriodicEffects, periodicCtx);
            }

            if (Removed) return; // 周期效果可能反噬自身（驱散/死亡清理）
            if (Def.Duration == EffectDurationKind.Rounds)
            {
                RemainingRounds--;
                if (RemainingRounds <= 0)
                {
                    if (Def.ExpirePerStack && StackCount > 1)
                    {
                        StackCount--;
                        RemainingRounds = Math.Max(1, Def.Rounds);
                        for (int i = 0; i < applied.Count; i++)
                        {
                            applied[i].StackCount = StackCount;
                            Target.Attributes.Touch(applied[i].Attribute);
                        }
                    }
                    else
                    {
                        Remove(engine);
                    }
                }
            }
        }

        /// <summary>移除：摘修饰（精准撤销）→ 摘 grantedTags（引用计数）→ 发移除事件。</summary>
        public void Remove(SkillEngine engine)
        {
            if (Removed) return;
            Removed = true;

            for (int i = 0; i < applied.Count; i++)
                Target.Attributes.RemoveModifier(applied[i]);
            applied.Clear();

            for (int i = 0; i < granted.Count; i++)
                Target.Tags.Remove(granted[i]);
            granted.Clear();

            engine.EnqueueEvent(SkillTags.EventEffectRemoved, Source, Target, StackCount, DamageSourceKind.None);
        }

        /// <summary>已授予标签的解析结果（驱散匹配用）。</summary>
        public IReadOnlyList<SkillTag> GrantedTags => granted;
    }

    /// <summary>
    /// 实体的持续效果容器：堆叠归属判定、应用/移除/周期驱动。
    /// 迭代用快照（pending 语义：处理期间新增的效果下一轮生效），移除即时标记、延迟压实。
    /// </summary>
    public sealed class ActiveEffectContainer
    {
        readonly SkillEntity owner;
        readonly List<ActiveEffectSpec> active = new List<ActiveEffectSpec>();

        public ActiveEffectContainer(SkillEntity owner) => this.owner = owner;

        public IReadOnlyList<ActiveEffectSpec> Active => active;

        /// <summary>施加或叠层（前置/免疫门禁在此）。返回是否生效。</summary>
        public bool ApplyOrStack(SkillEngine engine, GameplayEffectDef def, SkillEntity source, SkillContext ctx)
        {
            if (def == null) return false;

            if (def.ApplicationImmunityTags != null)
                foreach (string n in def.ApplicationImmunityTags)
                    if (!string.IsNullOrEmpty(n) && owner.Tags.Has(TagRegistry.Resolve(n))) return false;
            if (def.ApplicationRequiredTags != null)
                foreach (string n in def.ApplicationRequiredTags)
                    if (!string.IsNullOrEmpty(n) && !owner.Tags.Has(TagRegistry.Resolve(n))) return false;

            if (def.Duration == EffectDurationKind.Instant)
            {
                new ActiveEffectSpec { Def = def, Target = owner, Source = source }
                    .ApplyInstant(engine, ctx);
                return true;
            }

            ActiveEffectSpec stackable = FindStackable(def, source);
            if (stackable != null)
            {
                stackable.AddStack(); // 满层拒绝（溢出策略【预留】）
                return true;
            }

            ActiveEffectSpec spec = new ActiveEffectSpec { Def = def, Target = owner, Source = source };
            spec.Apply(engine, ctx);
            active.Add(spec);
            Compact();
            return true;
        }

        ActiveEffectSpec FindStackable(GameplayEffectDef def, SkillEntity source)
        {
            for (int i = 0; i < active.Count; i++)
            {
                ActiveEffectSpec s = active[i];
                if (s.Removed || !ReferenceEquals(s.Def, def)) continue;
                if (def.StackBy == StackAggregation.BySource && !ReferenceEquals(s.Source, source)) continue;
                return s;
            }
            return null;
        }

        /// <summary>回合时点：对快照逐效果 Tick（周期→递减→到期）。新挂效果本轮不 Tick。</summary>
        public void TickAll(SkillEngine engine)
        {
            ActiveEffectSpec[] snapshot = active.ToArray();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Tick(engine);
            Compact();
        }

        /// <summary>清除指定时长类效果（相位结束）。</summary>
        public void ExpireWhere(SkillEngine engine, Func<ActiveEffectSpec, bool> predicate)
        {
            ActiveEffectSpec[] snapshot = active.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                if (!snapshot[i].Removed && predicate(snapshot[i])) snapshot[i].Remove(engine);
            Compact();
        }

        /// <summary>按 grantedTags 匹配驱散（层级匹配）。</summary>
        public int DispelByTag(SkillEngine engine, SkillTag tag)
        {
            int count = 0;
            ActiveEffectSpec[] snapshot = active.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].Removed) continue;
                IReadOnlyList<SkillTag> granted = snapshot[i].GrantedTags;
                for (int j = 0; j < granted.Count; j++)
                    if (granted[j].Matches(tag)) { snapshot[i].Remove(engine); count++; break; }
            }
            Compact();
            return count;
        }

        /// <summary>撤销某来源施加在本实体身上的全部效果（光环撤除/离场清理）。</summary>
        public int RemoveFromSource(SkillEngine engine, SkillEntity source)
        {
            int count = 0;
            ActiveEffectSpec[] snapshot = active.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                if (!snapshot[i].Removed && ReferenceEquals(snapshot[i].Source, source))
                { snapshot[i].Remove(engine); count++; }
            Compact();
            return count;
        }

        /// <summary>移除全部（实体销毁用——事件照发，让触发器可见）。</summary>
        public void RemoveAll(SkillEngine engine)
        {
            ActiveEffectSpec[] snapshot = active.ToArray();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Remove(engine);
            Compact();
        }

        void Compact() => active.RemoveAll(s => s.Removed);
    }
}
