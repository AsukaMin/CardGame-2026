using System;
using System.Collections.Generic;
using Battle;

namespace CardAuthoring.Editor
{
    /// <summary>操作节点的编辑器描述：标题/菜单路径/摘要函数/额外分支端口。</summary>
    public class OpNodeDescriptor
    {
        public Type OpType;
        /// <summary>右键菜单路径，如 "操作/伤害"。</summary>
        public string MenuPath;
        /// <summary>节点标题。</summary>
        public string Title;
        /// <summary>额外分支输出端口（如 ConditionalOp 的 是/否）；"继续"端口所有操作默认拥有。</summary>
        public string[] BranchPorts = Array.Empty<string>();
        /// <summary>节点上的参数摘要（一行字）。</summary>
        public Func<EffectOp, string> Summarize;
    }

    /// <summary>
    /// 拓展点：新增效果操作的编辑器接入 = 一行 Register。
    /// 未注册的操作类型仍可被序列化/导出（数据层全自动），只是图上显示为类名、无摘要。
    /// 未来"动效节点"等新类目同样走注册表。
    /// </summary>
    public static class OpNodeRegistry
    {
        static readonly List<OpNodeDescriptor> all = new List<OpNodeDescriptor>();
        static readonly Dictionary<Type, OpNodeDescriptor> byType = new Dictionary<Type, OpNodeDescriptor>();

        static OpNodeRegistry()
        {
            Register(new OpNodeDescriptor
            {
                OpType = typeof(DamageOp), MenuPath = "操作/伤害", Title = "伤害",
                Summarize = op => $"造成 {((DamageOp)op).Amount} 点伤害",
            });
            Register(new OpNodeDescriptor
            {
                OpType = typeof(HealOp), MenuPath = "操作/治疗", Title = "治疗",
                Summarize = op => $"恢复 {((HealOp)op).Amount} 点生命",
            });
            Register(new OpNodeDescriptor
            {
                OpType = typeof(ModifyStatOp), MenuPath = "操作/属性修饰", Title = "属性修饰",
                Summarize = op =>
                {
                    ModifyStatOp m = (ModifyStatOp)op;
                    string stat = m.Stat == StatType.Attack ? "攻击" : "生命上限";
                    string dur = m.Duration == ModifierDurationKind.Battle ? ""
                        : m.Duration == ModifierDurationKind.ThisTurn ? "（本回合）" : $"（{m.TurnCount} 轮）";
                    return $"{stat} {(m.Amount >= 0 ? "+" : "")}{m.Amount}{dur}";
                },
            });
            Register(new OpNodeDescriptor
            {
                OpType = typeof(GainPointsOp), MenuPath = "操作/获得行动点", Title = "获得行动点",
                Summarize = op => $"+{((GainPointsOp)op).Amount} 点",
            });
            Register(new OpNodeDescriptor
            {
                OpType = typeof(DrawCardsOp), MenuPath = "操作/抽牌", Title = "抽牌",
                Summarize = op => $"抽 {((DrawCardsOp)op).Count} 张",
            });
            Register(new OpNodeDescriptor
            {
                OpType = typeof(GrantKeywordOp), MenuPath = "操作/赋予关键词", Title = "赋予关键词",
                Summarize = op =>
                {
                    GrantKeywordOp g = (GrantKeywordOp)op;
                    string dur = g.Duration == ModifierDurationKind.Battle ? "" : "（限时）";
                    return $"赋予 {g.Keyword}{dur}";
                },
            });
            Register(new OpNodeDescriptor
            {
                OpType = typeof(ConditionalOp), MenuPath = "操作/条件分支", Title = "条件分支",
                BranchPorts = new[] { PortNames.Then, PortNames.Else },
                Summarize = op =>
                {
                    ConditionalOp c = (ConditionalOp)op;
                    return c.Condition == null ? "（未设条件）" : ConditionDisplayName(c.Condition.GetType());
                },
            });
        }

        public static void Register(OpNodeDescriptor descriptor)
        {
            all.Add(descriptor);
            byType[descriptor.OpType] = descriptor;
        }

        public static IReadOnlyList<OpNodeDescriptor> All => all;

        public static OpNodeDescriptor Find(Type opType) =>
            byType.TryGetValue(opType, out OpNodeDescriptor d) ? d : null;

        public static EffectOp CreateOp(Type opType) => (EffectOp)Activator.CreateInstance(opType);

        /// <summary>条件的显示名（与 JSON 判别键同规则）。</summary>
        public static string ConditionDisplayName(Type t) => EffectJsonNames.Discriminator(t);
    }
}
