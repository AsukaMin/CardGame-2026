using System;

namespace Battle
{
    /// <summary>
    /// 目标选择模式（相对效果来源的 TeamId）。
    /// Instigator/EventTarget 仅触发上下文有效（事件引发者/承受者），普通上下文解析为空。
    /// </summary>
    public enum TargetMode
    {
        Chosen,              // 命令传入的主目标（ctx.MainTarget）
        Self,                // 效果来源实体
        Caster,              // 施法者——与 Self 同义（GAS 词汇兼容，配置可读性）
        Instigator,          // 事件的引发者（如攻击方）——仅触发上下文
        EventTarget,         // 事件的承受者（如受击方）——仅触发上下文
        AllAlliedUnits,      // 来源同阵营全体单位
        AllOpposingUnits,    // 来源敌对阵营全体单位
        AllUnits,            // 双方全体单位
        RandomOpposingUnit,  // 随机一个敌对阵营单位（经宿主随机源，确定性）
        AlliedLeader,        // 来源同阵营领袖（主战者）
        OpposingLeader,      // 敌对阵营领袖
    }

    /// <summary>数值来源。</summary>
    public enum ValueSourceKind
    {
        Const,          // 固定常量
        EventValue,     // 事件携带值（触发上下文的 Magnitude，如反弹等量伤害）
        SetByCaller,    // 上下文按键取值（宿主/上游传入）
        SourceAttribute,// 来源实体属性 × 系数（怪兽卡引用自身身材）
    }

    /// <summary>数值定义：Damage/Heal/修饰 的数值位统一形状。</summary>
    [Serializable]
    public sealed class ScopedValue
    {
        public ValueSourceKind Kind = ValueSourceKind.Const;
        /// <summary>Const 时的值。</summary>
        public double Value;
        /// <summary>SetByCaller 时的键名。</summary>
        public string Key = "";
        /// <summary>SourceAttribute 时的属性。</summary>
        public AttributeId Attribute = AttributeId.Attack;
        /// <summary>SourceAttribute 时的系数。</summary>
        public double Coefficient = 1.0;
        /// <summary>快照语义【预留】——true=应用时刻捕获，false（默认）=结算时实时。</summary>
        public bool UseSnapshot;

        public static ScopedValue Const(double value) => new ScopedValue { Value = value };
    }

    /// <summary>比较运算符。</summary>
    public enum CompareOperator { Greater, GreaterOrEqual, Less, LessOrEqual, Equal, NotEqual }

    /// <summary>比较值类型：Fixed=绝对值；PercentOfMaxHp=百分数（基准=求值对象自身 MaxHp）。</summary>
    public enum CompareValueType { Fixed, PercentOfMaxHp }

    /// <summary>条件求值对象：Current=被作用目标 / Source=施法者 / MainTarget=命令主目标。</summary>
    public enum ConditionTargetKind { Current, Source, MainTarget }

    /// <summary>条件基类（Data 层——求值归 ConditionSpec）。</summary>
    [Serializable]
    public abstract class ConditionDef { }

    /// <summary>属性比较：属性 × 运算符 × 比较值（固定数/百分比基准=自身 MaxHp）。</summary>
    [Serializable]
    public sealed class AttributeCompareCondition : ConditionDef
    {
        public AttributeId Attribute = AttributeId.CurrentHp;
        public CompareOperator Op = CompareOperator.LessOrEqual;
        public CompareValueType ValueType = CompareValueType.Fixed;
        /// <summary>Fixed=绝对数值；PercentOfMaxHp=百分数（50 表示 50%）。</summary>
        public double Value = 1;
    }

    /// <summary>事件值比较："受到 ≥3 伤害时"类过滤（对事件 Magnitude 求值）。</summary>
    [Serializable]
    public sealed class EventValueCondition : ConditionDef
    {
        public CompareOperator Op = CompareOperator.GreaterOrEqual;
        public double Value;
    }

    /// <summary>标签拥有条件："对有嘲讽的敌人"（层级匹配，可要求精确）。</summary>
    [Serializable]
    public sealed class HasTagCondition : ConditionDef
    {
        public string Tag = "";
        public bool Exact;
    }

    /// <summary>触发时机：After=事后响应；Preview=事前改写（仅 *.Preview 标签合法，注册时校验）。</summary>
    public enum TriggerTiming { After, Preview }

    /// <summary>触发器：按标签订阅事件（父标签命中全部子事件），命中执行效果组。</summary>
    [Serializable]
    public sealed class TriggerDef
    {
        public string EventTag = "";
        public TriggerTiming Timing = TriggerTiming.After;
        /// <summary>仅当事件承受者=宿主单位时触发（"我受伤时反弹"类个人被动的必备开关；世界级触发器忽略）。</summary>
        public bool SelfOnly;
        /// <summary>可选：对事件承受者求值的条件。</summary>
        public ConditionDef Filter;
        /// <summary>可选：对事件数值求值的过滤。</summary>
        public EventValueCondition ValueFilter;
        /// <summary>触发的效果组（Source=触发器宿主单位，owner 为空=世界级）。</summary>
        public EffectGroupDef Effects;
        /// <summary>每场一次。</summary>
        public bool Once;
    }

    /// <summary>效果组：目标选择 + Instant 操作序列（+表现数据位）。</summary>
    [Serializable]
    public sealed class EffectGroupDef
    {
        public TargetMode Target = TargetMode.Chosen;
        public EffectOp[] Ops = Array.Empty<EffectOp>();
        /// <summary>表现指令数据位——引擎不播放，宿主/表现层解释。</summary>
        public CueDef[] Cues;
        /// <summary>演出时序数据位【预留】：宿主与表现层协商的"动画后"结算标记。</summary>
        public bool PlayAfterAnimation;
    }

    /// <summary>表现指令（数据位）：Cue=指令名，Param=参数。</summary>
    [Serializable]
    public sealed class CueDef
    {
        public string Cue = "";
        public double Param;
    }
}
