using System;

namespace Battle
{
    /// <summary>目标选择模式（相对效果来源阵营）。</summary>
    public enum TargetMode
    {
        Chosen,            // 命令传入的目标（手牌效果卡由玩家指定）
        Self,              // 触发者自身单位（部位卡效果=所属单位）
        AllEnemyUnits,     // 对方全体单位
        AllFriendlyUnits,  // 我方全体单位
        AllUnits,          // 双方全体单位
        RandomEnemyUnit,   // 随机一个对方单位
        EnemyHero,         // 对方主战者
        FriendlyHero,      // 我方主战者
        Caster             // 施法者：部位卡效果=所属单位；效果卡=施法主战者（GAS 的 Caster 概念）
    }

    /// <summary>效果定义：目标选择 + 操作序列。数据驱动——新卡=新数据，不发版（板块划分文档硬要求）。</summary>
    [Serializable]
    public class EffectDef
    {
        public TargetMode Target = TargetMode.Chosen;
        public EffectOp[] Ops = Array.Empty<EffectOp>();

        /// <summary>演出标记：true=该组属"动画后"——默认立即结算；表现挂起模式（DeferPresentationEvents）下等待 EntranceAnimDone 事件放行。</summary>
        public bool PlayAfterAnimation;
    }

    /// <summary>效果操作基类：解释器按类型分发执行。</summary>
    [Serializable]
    public abstract class EffectOp { }

    [Serializable] public sealed class DamageOp : EffectOp { public int Amount; }

    [Serializable] public sealed class HealOp : EffectOp { public int Amount; }

    /// <summary>属性修饰：攻/血上限增减（含时长）。</summary>
    [Serializable]
    public sealed class ModifyStatOp : EffectOp
    {
        public StatType Stat;
        public int Amount;
        public ModifierDurationKind Duration = ModifierDurationKind.Battle;
        /// <summary>Duration==Turns 时的持续轮数。</summary>
        public int TurnCount = 1;
    }

    /// <summary>获得行动点（仅玩家方效果生效；敌方不走行动点）。</summary>
    [Serializable]
    public sealed class GainPointsOp : EffectOp { public int Amount; }

    /// <summary>抽牌（仅玩家方）。</summary>
    [Serializable]
    public sealed class DrawCardsOp : EffectOp { public int Count = 1; }

    /// <summary>赋予关键词（含时长）。</summary>
    [Serializable]
    public sealed class GrantKeywordOp : EffectOp
    {
        public Keywords Keyword = Keywords.Taunt;
        public ModifierDurationKind Duration = ModifierDurationKind.Battle;
        public int TurnCount = 1;
    }

    /// <summary>条件分支：条件按 ConditionTarget 指定的对象求值，命中走 Then，否则走 Else。例："对半血以下敌人伤害翻倍"。</summary>
    [Serializable]
    public sealed class ConditionalOp : EffectOp
    {
        public ConditionDef Condition;

        /// <summary>条件求值对象：Current=被作用目标 / Caster=施法者 / MainTarget=技能主目标（命令传入）。</summary>
        public ConditionTargetKind ConditionTarget = ConditionTargetKind.Current;

        public EffectOp[] Then = Array.Empty<EffectOp>();
        public EffectOp[] Else = Array.Empty<EffectOp>();
    }

    /// <summary>条件求值对象。</summary>
    public enum ConditionTargetKind { Current, Caster, MainTarget }

    /// <summary>条件基类。</summary>
    [Serializable]
    public abstract class ConditionDef { }

    /// <summary>属性比较条件：属性 × 运算符 × 比较值（固定数 / 百分比——基准=比较对象自身生命上限）。</summary>
    [Serializable]
    public sealed class AttributeCompareCondition : ConditionDef
    {
        public ConditionAttr Attr = ConditionAttr.Hp;
        public CompareOperator Op = CompareOperator.LessOrEqual;
        public CompareValueType ValueType = CompareValueType.Fixed;

        /// <summary>比较值：Fixed=绝对数值；PercentOfMaxHp=百分数（50 表示 50%）。</summary>
        public double Value = 1;
    }

    /// <summary>可比较属性（主战者的攻击视为 0——主战者不攻击，既定规则）。</summary>
    public enum ConditionAttr { Hp, MaxHp, Attack }

    /// <summary>比较运算符。</summary>
    public enum CompareOperator { Greater, GreaterOrEqual, Less, LessOrEqual, Equal, NotEqual }

    /// <summary>比较值类型。</summary>
    public enum CompareValueType { Fixed, PercentOfMaxHp }

    /// <summary>解析后的效果目标：单位或主战者（二选一）。主战者 Id 约定见 BattleSession。</summary>
    public readonly struct EffectTarget
    {
        public readonly BattleUnit Unit;
        public readonly Side HeroSide;

        public EffectTarget(BattleUnit unit) { Unit = unit; HeroSide = default; }
        public EffectTarget(Side heroSide) { Unit = null; HeroSide = heroSide; }

        public bool IsUnit => Unit != null;
        public int Id => Unit != null
            ? Unit.Id
            : (HeroSide == Side.Player ? BattleSession.PlayerHeroId : BattleSession.EnemyHeroId);
    }
}
