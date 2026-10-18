using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 通用事件载荷（GAS GameplayEvent 的标签化变体）：事件种类即标签，触发器按标签订阅。
    /// 事后事件入 FIFO 队列排空；事前（*.Preview）事件同步派发、可改写待结算数值。
    /// </summary>
    public readonly struct GameplayEvent
    {
        public readonly SkillTag Tag;
        /// <summary>引发者（攻击方/效果来源），可空。</summary>
        public readonly SkillEntity Instigator;
        /// <summary>承受者（受击方/效果宿主），可空。</summary>
        public readonly SkillEntity Target;
        public readonly double Magnitude;
        public readonly DamageSourceKind Kind;

        public GameplayEvent(SkillTag tag, SkillEntity instigator, SkillEntity target,
            double magnitude, DamageSourceKind kind)
        {
            Tag = tag;
            Instigator = instigator;
            Target = target;
            Magnitude = magnitude;
            Kind = kind;
        }

        public bool IsValid => Tag.IsValid;
    }

    /// <summary>待结算伤害（Preview 阶段可改写：Amount' = Amount×Scale + Delta，下限 0）。</summary>
    public sealed class DamageContext
    {
        public SkillEntity Source;
        public SkillEntity Target;
        public double Amount;
        public DamageSourceKind Kind;
    }

    /// <summary>待结算治疗（Preview 阶段可改写）。</summary>
    public sealed class HealContext
    {
        public SkillEntity Source;
        public SkillEntity Target;
        public double Amount;
    }

    /// <summary>
    /// 技能引擎门面（宿主持有的实例，随宿主生灭）：
    /// 效果执行 / 伤害治疗管线三段 / 事件队列与触发 / 回合时点推进 / 来源撤除。
    /// 同步结算模型——所有入口在返回前排空事件队列；整链派发深度防护 64（防数据造环）。
    /// </summary>
    public sealed class SkillEngine
    {
        const int MaxDispatchDepth = 64;

        sealed class TriggerEntry
        {
            public SkillEntity Owner;   // null=世界级（宿主自设触发，如死亡观察）
            public TriggerDef Def;
            public SkillTag Tag;
            public bool Fired;          // Once 用
        }

        readonly ISkillWorld world;
        readonly Dictionary<Type, Func<EffectOp, OpSpec>> customOps = new Dictionary<Type, Func<EffectOp, OpSpec>>();
        readonly List<TriggerEntry> triggers = new List<TriggerEntry>(); // 注册顺序=触发顺序
        readonly Queue<GameplayEvent> queue = new Queue<GameplayEvent>();

        bool inSession;
        int dispatchCount;
        DamageContext pendingDamage;   // 当前 Preview 载荷（嵌套时保存恢复）
        HealContext pendingHeal;

        public SkillEngine(ISkillWorld world) =>
            this.world = world ?? throw new ArgumentNullException(nameof(world));

        /// <summary>深度防护触发（链式派发超上限被丢弃的次数）——测试与宿主监控用。</summary>
        public int DroppedDispatches { get; private set; }

        /// <summary>世界视图（宿主自定义 op 需要触达世界时取用）。</summary>
        internal ISkillWorld World => world;

        // ==================== 主动执行 ====================

        /// <summary>执行一组效果组（效果卡打出/入场槽触发/意图施法的入口）。</summary>
        public void Execute(EffectGroupDef[] groups, SkillContext ctx)
        {
            if (groups == null) return;
            Session(() =>
            {
                foreach (EffectGroupDef g in groups) ExecuteGroup(g, ctx);
            });
        }

        /// <summary>执行单个效果组：解析目标 → 逐目标切换 CurrentTarget → 逐 op 分发。</summary>
        public void ExecuteGroup(EffectGroupDef group, SkillContext ctx)
        {
            if (group == null || group.Ops == null || group.Ops.Length == 0) return;
            List<SkillEntity> targets = ResolveTargets(group.Target, ctx);
            foreach (SkillEntity target in targets)
            {
                ctx.CurrentTarget = target;
                ExecuteOps(group.Ops, ctx);
            }
            ctx.CurrentTarget = null;
        }

        /// <summary>逐 op 分发（宿主自定义 op 优先，其次内置工厂，未知类型=配置错误）。</summary>
        public void ExecuteOps(EffectOp[] ops, SkillContext ctx)
        {
            if (ops == null) return;
            foreach (EffectOp op in ops)
            {
                if (op == null) continue;
                OpSpec spec = CreateOp(op);
                spec.Execute(this, ctx);
            }
        }

        OpSpec CreateOp(EffectOp op)
        {
            if (customOps.TryGetValue(op.GetType(), out Func<EffectOp, OpSpec> factory))
                return factory(op);
            OpSpec spec = SpecFactory.CreateOp(op);
            if (spec == null)
                throw new InvalidOperationException(
                    $"未知操作类型 {op.GetType().Name}——宿主自定义 op 须先 RegisterOp（配置错误）");
            return spec;
        }

        /// <summary>注册宿主自定义 op 的工厂（玩法 op：抽牌/得点等）。</summary>
        public void RegisterOp<TOp>(Func<EffectOp, OpSpec> factory) where TOp : EffectOp =>
            customOps[typeof(TOp)] = factory ?? throw new ArgumentNullException(nameof(factory));

        // ==================== 目标解析 ====================

        public List<SkillEntity> ResolveTargets(TargetMode mode, SkillContext ctx)
        {
            List<SkillEntity> targets = new List<SkillEntity>();
            SkillEntity source = ctx.Source;
            int team = source?.TeamId ?? 0;

            switch (mode)
            {
                case TargetMode.Chosen:
                    if (ctx.MainTarget != null) targets.Add(ctx.MainTarget);
                    break;
                case TargetMode.Self:
                case TargetMode.Caster:
                    if (source != null) targets.Add(source);
                    break;
                case TargetMode.Instigator:
                    if (ctx.Event.IsValid && ctx.Event.Instigator != null) targets.Add(ctx.Event.Instigator);
                    break;
                case TargetMode.EventTarget:
                    if (ctx.Event.IsValid && ctx.Event.Target != null) targets.Add(ctx.Event.Target);
                    break;
                case TargetMode.AllAlliedUnits:
                    targets.AddRange(world.EntitiesOfTeam(team, EntityKind.Unit));
                    break;
                case TargetMode.AllOpposingUnits:
                    targets.AddRange(world.EntitiesNotOfTeam(team, EntityKind.Unit));
                    break;
                case TargetMode.AllUnits:
                    foreach (SkillEntity e in world.AllEntities)
                        if (e.Kind == EntityKind.Unit) targets.Add(e);
                    break;
                case TargetMode.RandomOpposingUnit:
                {
                    IReadOnlyList<SkillEntity> foes = world.EntitiesNotOfTeam(team, EntityKind.Unit);
                    if (foes.Count > 0) targets.Add(world.Random.Pick(foes));
                    break;
                }
                case TargetMode.AlliedLeader:
                {
                    SkillEntity leader = world.LeaderOfTeam(team);
                    if (leader != null) targets.Add(leader);
                    break;
                }
                case TargetMode.OpposingLeader:
                {
                    SkillEntity leader = world.LeaderOfTeam(world.OpposingTeamOf(team));
                    if (leader != null) targets.Add(leader);
                    break;
                }
            }
            return targets;
        }

        // ==================== 数值求值 ====================

        public double Evaluate(ScopedValue value, SkillContext ctx)
        {
            if (value == null) return 0;
            switch (value.Kind)
            {
                case ValueSourceKind.Const: return value.Value;
                case ValueSourceKind.EventValue: return ctx.Event.IsValid ? ctx.Event.Magnitude : 0;
                case ValueSourceKind.SetByCaller: return ctx.GetSetByCaller(value.Key);
                case ValueSourceKind.SourceAttribute:
                    return (ctx.Source != null ? ctx.Source.Attributes.Current(value.Attribute) : 0)
                        * value.Coefficient;
                default: return 0;
            }
        }

        // ==================== 伤害/治疗管线（三段） ====================

        /// <summary>伤害：Preview 聚合改写 → 落地（改 CurrentHp 的 Base）→ Applied 事件入队。</summary>
        public DamageResult ResolveDamage(SkillEntity source, SkillEntity target, double amount,
            DamageSourceKind kind = DamageSourceKind.Effect)
        {
            DamageResult result = default;
            if (target == null) return result;
            Session(() =>
            {
                DamageContext ctx = new DamageContext
                {
                    Source = source, Target = target,
                    Amount = Math.Max(0, amount), Kind = kind,
                };
                DamageContext saved = pendingDamage;
                pendingDamage = ctx;
                try
                {
                    DispatchPreview(new GameplayEvent(
                        SkillTags.EventDamagePreview, source, target, ctx.Amount, kind));
                }
                finally { pendingDamage = saved; }

                double final = Math.Max(0, ctx.Amount);
                double before = target.Attributes.Current(AttributeId.CurrentHp);
                target.Attributes.SetBase(AttributeId.CurrentHp,
                    target.Attributes.Base(AttributeId.CurrentHp) - final);
                double actual = before - target.Attributes.Current(AttributeId.CurrentHp);
                EnqueueEvent(SkillTags.EventDamageApplied, source, target, actual, kind);
                result = new DamageResult(target, actual);
            });
            return result;
        }

        /// <summary>治疗：Preview 改写 → 落地（上限夹紧）→ Applied 事件入队。</summary>
        public HealResult ResolveHeal(SkillEntity source, SkillEntity target, double amount)
        {
            HealResult result = default;
            if (target == null) return result;
            Session(() =>
            {
                HealContext ctx = new HealContext
                {
                    Source = source, Target = target, Amount = Math.Max(0, amount),
                };
                HealContext saved = pendingHeal;
                pendingHeal = ctx;
                try
                {
                    DispatchPreview(new GameplayEvent(
                        SkillTags.EventHealPreview, source, target, ctx.Amount, DamageSourceKind.None));
                }
                finally { pendingHeal = saved; }

                double before = target.Attributes.Current(AttributeId.CurrentHp);
                double max = target.Attributes.Current(AttributeId.MaxHp);
                target.Attributes.SetBase(AttributeId.CurrentHp,
                    Math.Min(target.Attributes.Base(AttributeId.CurrentHp) + Math.Max(0, ctx.Amount), max));
                double actual = target.Attributes.Current(AttributeId.CurrentHp) - before;
                EnqueueEvent(SkillTags.EventHealApplied, source, target, actual, DamageSourceKind.None);
                result = new HealResult(target, actual);
            });
            return result;
        }

        /// <summary>改写待结算数值（仅 Preview 触发上下文合法——ModifyEventMagnitudeOp 的落点）。</summary>
        public void ModifyPendingMagnitude(double delta, double scale)
        {
            if (pendingDamage != null)
                pendingDamage.Amount = Math.Max(0, pendingDamage.Amount * scale + delta);
            else if (pendingHeal != null)
                pendingHeal.Amount = Math.Max(0, pendingHeal.Amount * scale + delta);
            else
                throw new InvalidOperationException(
                    "ModifyEventMagnitudeOp 只在 Preview 触发上下文合法（配置错误）");
        }

        // ==================== 触发器 ====================

        /// <summary>注册触发器（owner 可空=世界级；注册顺序=触发顺序，确定性由宿主掌握）。</summary>
        public void RegisterTrigger(SkillEntity owner, TriggerDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.EventTag))
                throw new InvalidOperationException("TriggerDef.EventTag 为空（配置错误）");
            SkillTag tag = TagRegistry.Resolve(def.EventTag);
            bool isPreviewTag = tag.Name.EndsWith(SkillTags.PreviewSuffix, StringComparison.Ordinal);
            if (def.Timing == TriggerTiming.Preview && !isPreviewTag)
                throw new InvalidOperationException(
                    $"Timing.Preview 只允许订阅 *.Preview 标签， got \"{tag.Name}\"（配置错误）");
            triggers.Add(new TriggerEntry { Owner = owner, Def = def, Tag = tag });
        }

        /// <summary>注销该实体名下全部触发器（离场）。</summary>
        public void UnregisterTriggers(SkillEntity owner) =>
            triggers.RemoveAll(t => ReferenceEquals(t.Owner, owner));

        /// <summary>宿主喂战斗语义事件（回合开始/单位死亡/抽牌……引擎不知其含义）。入队并排空。</summary>
        public void Raise(GameplayEvent evt)
        {
            if (!evt.IsValid) return;
            Session(() => EnqueueEvent(evt));
        }

        internal void EnqueueEvent(SkillTag tag, SkillEntity instigator, SkillEntity target,
            double magnitude, DamageSourceKind kind) =>
            queue.Enqueue(new GameplayEvent(tag, instigator, target, magnitude, kind));

        void EnqueueEvent(GameplayEvent evt) => queue.Enqueue(evt);

        /// <summary>派发事后事件（队列出队后调用）：快照触发器表，按标签订阅/过滤条件/Once 匹配。</summary>
        void DispatchAfter(GameplayEvent evt)
        {
            TriggerEntry[] snapshot = triggers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                TriggerEntry entry = snapshot[i];
                if (entry.Def.Timing != TriggerTiming.After || entry.Fired) continue;
                if (entry.Def.SelfOnly && !ReferenceEquals(evt.Target, entry.Owner)) continue;
                if (!evt.Tag.Matches(entry.Tag)) continue; // 事件标签拥有订阅标签（父订阅捕获子事件）

                SkillContext filterCtx = new SkillContext
                {
                    Source = entry.Owner, CurrentTarget = evt.Target, Event = evt,
                };
                if (entry.Def.Filter != null)
                {
                    ConditionSpec filter = SpecFactory.CreateCondition(entry.Def.Filter);
                    if (filter != null && !filter.Eval(filterCtx, evt.Target)) continue;
                }
                if (entry.Def.ValueFilter != null)
                {
                    ConditionSpec vf = SpecFactory.CreateCondition(entry.Def.ValueFilter);
                    if (vf != null && !vf.Eval(filterCtx, evt.Target)) continue;
                }
                if (entry.Def.Once) entry.Fired = true;

                if (entry.Def.Effects != null)
                {
                    SkillContext ctx = new SkillContext { Source = entry.Owner, Event = evt };
                    ExecuteGroup(entry.Def.Effects, ctx);
                }
            }
        }

        /// <summary>同步派发事前事件（Preview 触发器在此改写待结算数值）。</summary>
        void DispatchPreview(GameplayEvent evt)
        {
            if (++dispatchCount > MaxDispatchDepth) { DroppedDispatches++; return; }
            TriggerEntry[] snapshot = triggers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                TriggerEntry entry = snapshot[i];
                if (entry.Def.Timing != TriggerTiming.Preview || entry.Fired) continue;
                if (entry.Def.SelfOnly && !ReferenceEquals(evt.Target, entry.Owner)) continue;
                if (!evt.Tag.Matches(entry.Tag)) continue;

                SkillContext filterCtx = new SkillContext
                {
                    Source = entry.Owner, CurrentTarget = evt.Target, Event = evt,
                };
                if (entry.Def.Filter != null)
                {
                    ConditionSpec filter = SpecFactory.CreateCondition(entry.Def.Filter);
                    if (filter != null && !filter.Eval(filterCtx, evt.Target)) continue;
                }
                if (entry.Def.ValueFilter != null)
                {
                    ConditionSpec vf = SpecFactory.CreateCondition(entry.Def.ValueFilter);
                    if (vf != null && !vf.Eval(filterCtx, evt.Target)) continue;
                }
                if (entry.Def.Once) entry.Fired = true;

                if (entry.Def.Effects != null)
                {
                    SkillContext ctx = new SkillContext { Source = entry.Owner, Event = evt };
                    ExecuteGroup(entry.Def.Effects, ctx);
                }
            }
        }

        // ==================== 回合时点推进（宿主调用） ====================

        /// <summary>相位结束（战斗=回合结束）：清除全部 ThisPhase 效果。</summary>
        public void ExpirePhase()
        {
            Session(() =>
            {
                foreach (SkillEntity e in world.AllEntities)
                    e.Effects.ExpireWhere(this, s => s.Def.Duration == EffectDurationKind.ThisPhase);
            });
        }

        /// <summary>轮次开始（战斗=回合开始）：周期执行 → Rounds 递减 → 到期（掉层或移除）。</summary>
        public void TickRound()
        {
            Session(() =>
            {
                foreach (SkillEntity e in world.AllEntities)
                    e.Effects.TickAll(this);
            });
        }

        // ==================== 撤除与清理 ====================

        /// <summary>撤销该实体施加的全部效果（光环撤除：宿主在单位离场时调用，遍历世界全体）。</summary>
        public void RemoveEffectsFromSource(SkillEntity source)
        {
            Session(() =>
            {
                foreach (SkillEntity e in world.AllEntities)
                    e.Effects.RemoveFromSource(this, source);
            });
        }

        /// <summary>移除某实体身上的全部效果（离场清理；事件照发让触发器可见）。</summary>
        public void RemoveEntityEffects(SkillEntity entity)
        {
            Session(() => entity.Effects.RemoveAll(this));
        }

        /// <summary>按标签驱散目标身上的持续效果（RemoveTagOp 的落点；层级匹配）。</summary>
        public int DispelByTag(SkillEntity target, SkillTag tag)
        {
            int count = 0;
            Session(() => { count = target.Effects.DispelByTag(this, tag); });
            return count;
        }

        // ==================== 隐式持续效果（op 包装用） ====================

        internal void ApplyTransientModifier(SkillEntity target, SkillEntity source,
            AttributeId attribute, ModOp op, double magnitude, EffectDurationKind duration, int rounds)
        {
            GameplayEffectDef def = new GameplayEffectDef
            {
                Name = "隐式修饰",
                Duration = duration == EffectDurationKind.Instant ? EffectDurationKind.Permanent : duration,
                Rounds = Math.Max(1, rounds),
                Modifiers = new[] { new AttributeModifierDef { Attribute = attribute, Op = op, Magnitude = ScopedValue.Const(magnitude) } },
            };
            target.Effects.ApplyOrStack(this, def, source, new SkillContext { Source = source, CurrentTarget = target });
        }

        internal void ApplyTransientTag(SkillEntity target, SkillEntity source,
            SkillTag tag, EffectDurationKind duration, int rounds)
        {
            GameplayEffectDef def = new GameplayEffectDef
            {
                Name = $"隐式标签 {tag.Name}",
                Duration = duration == EffectDurationKind.Instant ? EffectDurationKind.Permanent : duration,
                Rounds = Math.Max(1, rounds),
                GrantedTags = new[] { tag.Name },
            };
            target.Effects.ApplyOrStack(this, def, source, new SkillContext { Source = source, CurrentTarget = target });
        }

        // ==================== 会话与队列 ====================

        /// <summary>
        /// 会话语义：最外层入口在动作完成后排空事件队列；嵌套入口只做活不动队列（链式反应由外层统一排空）。
        /// 派发深度（Preview+After 合计）超 64 即丢弃后续派发并计数——防数据造环。
        /// </summary>
        void Session(Action work)
        {
            if (inSession) { work(); return; }
            inSession = true;
            int savedDispatchCount = dispatchCount;
            dispatchCount = 0;
            try
            {
                work();
                while (queue.Count > 0)
                {
                    if (++dispatchCount > MaxDispatchDepth) { DroppedDispatches++; break; }
                    GameplayEvent evt = queue.Dequeue();
                    DispatchAfter(evt);
                }
            }
            finally
            {
                queue.Clear();
                dispatchCount = savedDispatchCount;
                inSession = false;
            }
        }
    }
}
