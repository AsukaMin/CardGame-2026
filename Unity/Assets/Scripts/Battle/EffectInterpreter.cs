using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 效果解释器：执行数据驱动的 EffectDef/EffectOp（通用效果解释器，新卡=新数据）。
    /// 伤害/治疗经由 BattleSession 统一入口——事件广播、死亡结算、胜负检查都集中在那里。
    /// </summary>
    internal class EffectInterpreter
    {
        readonly BattleSession session;

        public EffectInterpreter(BattleSession session) => this.session = session;

        /// <summary>执行一组效果。sourceSide/sourceUnitId 为效果来源；chosenTargetId 为玩家指定目标（技能主目标）。</summary>
        public void Execute(EffectDef[] defs, Side sourceSide, int? sourceUnitId, int? chosenTargetId)
        {
            if (defs == null) return;
            foreach (EffectDef def in defs)
            {
                if (def == null || def.Ops == null || def.Ops.Length == 0) continue;
                if (session.TryDeferForPresentation(def, sourceSide, sourceUnitId, chosenTargetId)) continue;
                List<EffectTarget> targets = ResolveTargets(def.Target, sourceSide, sourceUnitId, chosenTargetId);
                foreach (EffectTarget target in targets)
                    RunOps(def.Ops, target, sourceSide, sourceUnitId, chosenTargetId);
            }
        }

        void RunOps(EffectOp[] ops, EffectTarget target, Side sourceSide, int? sourceUnitId, int? chosenTargetId)
        {
            foreach (EffectOp op in ops)
                ApplyOp(op, target, sourceSide, sourceUnitId, chosenTargetId);
        }

        void ApplyOp(EffectOp op, EffectTarget target, Side sourceSide, int? sourceUnitId, int? chosenTargetId)
        {
            switch (op)
            {
                case DamageOp damage:
                    session.ApplyDamage(target, damage.Amount);
                    break;

                case HealOp heal:
                    session.ApplyHeal(target, heal.Amount);
                    break;

                case ModifyStatOp mod:
                    if (target.IsUnit)
                        target.Unit.AddTransient(new StatModifier
                        {
                            Stat = mod.Stat,
                            Amount = mod.Amount,
                            Kind = mod.Duration,
                            RemainingTurns = mod.TurnCount,
                        });
                    break;

                case GrantKeywordOp grant:
                    if (target.IsUnit)
                        target.Unit.AddTransient(new StatModifier
                        {
                            IsKeywordMod = true,
                            Keyword = grant.Keyword,
                            Kind = grant.Duration,
                            RemainingTurns = grant.TurnCount,
                        });
                    break;

                case GainPointsOp gain:
                    // 敌方不走行动点（PvE 不对称）
                    if (sourceSide == Side.Player) session.Economy.Grant(gain.Amount);
                    break;

                case DrawCardsOp draw:
                    if (sourceSide == Side.Player) session.DrawCards(draw.Count);
                    break;

                case ConditionalOp cond:
                    EffectTarget conditionObj = ResolveConditionTarget(
                        cond.ConditionTarget, target, sourceSide, sourceUnitId, chosenTargetId);
                    EffectOp[] branch = EvalCondition(cond.Condition, conditionObj) ? cond.Then : cond.Else;
                    if (branch != null) RunOps(branch, target, sourceSide, sourceUnitId, chosenTargetId);
                    break;
            }
        }

        bool EvalCondition(ConditionDef condition, EffectTarget target)
        {
            if (condition == null) return true;
            if (condition is AttributeCompareCondition cmp)
            {
                double left = AttrValue(target, cmp.Attr);
                double rhs = cmp.ValueType == CompareValueType.PercentOfMaxHp
                    ? MaxHpOf(target) * cmp.Value / 100.0
                    : cmp.Value;
                switch (cmp.Op)
                {
                    case CompareOperator.Greater: return left > rhs;
                    case CompareOperator.GreaterOrEqual: return left >= rhs;
                    case CompareOperator.Less: return left < rhs;
                    case CompareOperator.LessOrEqual: return left <= rhs;
                    case CompareOperator.Equal: return Math.Abs(left - rhs) < 1e-9;
                    case CompareOperator.NotEqual: return Math.Abs(left - rhs) >= 1e-9;
                    default: return false;
                }
            }
            return false;
        }

        /// <summary>属性取值：主战者的攻击视为 0（主战者不攻击，既定规则）。</summary>
        double AttrValue(EffectTarget t, ConditionAttr attr)
        {
            if (!t.IsUnit)
            {
                return attr == ConditionAttr.Hp ? session.HpOf(t.HeroSide)
                    : attr == ConditionAttr.MaxHp ? session.MaxHpOf(t.HeroSide)
                    : 0;
            }
            return attr == ConditionAttr.Hp ? t.Unit.Hp
                : attr == ConditionAttr.MaxHp ? t.Unit.EffectiveMaxHp
                : t.Unit.EffectiveAttack;
        }

        /// <summary>条件求值对象：Current=被作用目标 / Caster=施法者 / MainTarget=技能主目标（命令传入）。</summary>
        EffectTarget ResolveConditionTarget(ConditionTargetKind kind, EffectTarget current,
            Side sourceSide, int? sourceUnitId, int? chosenTargetId)
        {
            EffectTarget? picked =
                kind == ConditionTargetKind.Caster ? FirstOf(TargetMode.Caster, sourceSide, sourceUnitId, chosenTargetId)
                : kind == ConditionTargetKind.MainTarget ? FirstOf(TargetMode.Chosen, sourceSide, sourceUnitId, chosenTargetId)
                : (EffectTarget?)null;
            return picked ?? current;
        }

        EffectTarget? FirstOf(TargetMode mode, Side sourceSide, int? sourceUnitId, int? chosenTargetId)
        {
            List<EffectTarget> targets = ResolveTargets(mode, sourceSide, sourceUnitId, chosenTargetId);
            return targets.Count > 0 ? (EffectTarget?)targets[0] : null;
        }

        int HpOf(EffectTarget t) => t.IsUnit ? t.Unit.Hp : session.HpOf(t.HeroSide);
        int MaxHpOf(EffectTarget t) => t.IsUnit ? t.Unit.EffectiveMaxHp : session.MaxHpOf(t.HeroSide);

        /// <summary>解析目标选择（相对来源阵营）。</summary>
        internal List<EffectTarget> ResolveTargets(
            TargetMode mode, Side sourceSide, int? sourceUnitId, int? chosenTargetId)
        {
            List<EffectTarget> targets = new List<EffectTarget>();
            Side foe = sourceSide == Side.Player ? Side.Enemy : Side.Player;

            switch (mode)
            {
                case TargetMode.Chosen:
                    if (chosenTargetId.HasValue && chosenTargetId.Value > 0)
                    {
                        BattleUnit unit = session.Field.GetUnit(chosenTargetId.Value);
                        if (unit != null) targets.Add(new EffectTarget(unit));
                        else if (chosenTargetId.Value == BattleSession.PlayerHeroId) targets.Add(new EffectTarget(Side.Player));
                        else if (chosenTargetId.Value == BattleSession.EnemyHeroId) targets.Add(new EffectTarget(Side.Enemy));
                    }
                    break;

                case TargetMode.Self:
                    if (sourceUnitId.HasValue)
                    {
                        BattleUnit self = session.Field.GetUnit(sourceUnitId.Value);
                        if (self != null) targets.Add(new EffectTarget(self));
                    }
                    break;

                case TargetMode.AllEnemyUnits:
                    foreach (BattleUnit u in session.Field.UnitsOf(foe))
                        targets.Add(new EffectTarget(u));
                    break;

                case TargetMode.AllFriendlyUnits:
                    foreach (BattleUnit u in session.Field.UnitsOf(sourceSide))
                        targets.Add(new EffectTarget(u));
                    break;

                case TargetMode.AllUnits:
                    foreach (BattleUnit u in session.Field.AllUnits)
                        targets.Add(new EffectTarget(u));
                    break;

                case TargetMode.RandomEnemyUnit:
                    List<BattleUnit> foes = session.Field.UnitsOf(foe);
                    if (foes.Count > 0) targets.Add(new EffectTarget(session.Rng.Pick(foes)));
                    break;

                case TargetMode.EnemyHero:
                    targets.Add(new EffectTarget(foe));
                    break;

                case TargetMode.FriendlyHero:
                    targets.Add(new EffectTarget(sourceSide));
                    break;

                case TargetMode.Caster:
                    // 施法者：来源单位（部位卡效果=所属单位）优先，否则来源方主战者（效果卡）
                    if (sourceUnitId.HasValue)
                    {
                        BattleUnit caster = session.Field.GetUnit(sourceUnitId.Value);
                        if (caster != null) targets.Add(new EffectTarget(caster));
                    }
                    else
                    {
                        targets.Add(new EffectTarget(sourceSide));
                    }
                    break;
            }
            return targets;
        }

        /// <summary>档位是否需要玩家指定目标。</summary>
        internal static bool NeedsChosenTarget(EffectTier tier)
        {
            foreach (EffectDef def in tier.Effects)
                if (def != null && def.Target == TargetMode.Chosen) return true;
            return false;
        }

        /// <summary>档位是否需要"单位"目标（属性/关键词操作对主战者无意义）。</summary>
        internal static bool NeedsUnitTarget(EffectTier tier)
        {
            foreach (EffectDef def in tier.Effects)
                if (def != null && def.Target == TargetMode.Chosen && ContainsUnitOnlyOp(def.Ops)) return true;
            return false;
        }

        static bool ContainsUnitOnlyOp(EffectOp[] ops)
        {
            if (ops == null) return false;
            foreach (EffectOp op in ops)
            {
                switch (op)
                {
                    case ModifyStatOp _:
                    case GrantKeywordOp _:
                        return true;
                    case ConditionalOp cond:
                        if (ContainsUnitOnlyOp(cond.Then) || ContainsUnitOnlyOp(cond.Else)) return true;
                        break;
                }
            }
            return false;
        }
    }
}
