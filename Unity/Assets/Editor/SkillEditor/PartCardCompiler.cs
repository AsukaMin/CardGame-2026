using System;
using System.Collections.Generic;
using System.Linq;
using Battle;
using Fusion;

/// <summary>
/// 图 → 数据编译器（M5·T5' 核心）：PartCardAuthoringAsset（图+坐标）→ PartCardConfig（parts.json 条目）。
/// 纯内存转换、零 UnityEditor 依赖；校验错误收集进列表（导出时汇总报出，编译返回 null）。
/// 映射规则（技术设计§四）：
/// - 节点目标/选择目标节点 = 效果组边界（目标切换开新组，组序=链序）；
/// - 并行分支（输出多连）= 同层多组，执行序=图上 X 坐标左→右；
/// - 条件分支 = ConditionalOp（Then/Else=纯 op 子链——分支内禁切目标，连线层与编译双重校验）；
/// - 常驻「触发」链：链首触发配置节点 → TriggerDef（单组单目标——TriggerDef.Effects 是单个效果组）；
/// - 常驻「光环」链：属性修饰/赋予关键词 → GameplayEffectDef（Permanent，随宿主生命周期撤销）。
/// </summary>
public static class PartCardCompiler
{
    /// <summary>编译一张卡。有错误时返回 null 且 errors 非空（错误带节点定位）。</summary>
    public static PartCardConfig Compile(PartCardAuthoringAsset card, string cardName, List<string> errors)
    {
        if (card == null) { errors.Add("卡为空"); return null; }
        Map map = new Map(card);

        PartCardConfig config = new PartCardConfig
        {
            Id = card.CardId,
            Name = cardName, // 卡名 = 文件名（需求文档§3.1）
            SlotType = ParseSlot(card.SlotType, errors),
            Cost = card.Cost,
            Attack = card.SlotType == "Body" ? card.Attack : 0,
            MaxHp = card.SlotType == "Body" ? card.MaxHp : 0,
            GrantTags = SplitTags(card.GrantTags),
            Consumable = card.Consumable,
        };

        List<EffectGroupDef> effects = new List<EffectGroupDef>();
        List<TriggerDef> triggers = new List<TriggerDef>();
        List<GameplayEffectDef> auras = new List<GameplayEffectDef>();

        if (card.SlotType == "OnSummon")
        {
            WalkRootChains(map, RootPorts.OnSummon, card.DefaultTarget, effects, errors, playAfterAnimation: false);
            WalkRootChains(map, RootPorts.OnSummonLate, card.DefaultTarget, effects, errors, playAfterAnimation: true);
        }
        else if (card.SlotType == "Continuous")
        {
            WalkTriggerChains(map, triggers, errors);
            WalkAuraChains(map, auras, errors);
        }
        else if (card.SlotType == "OnLeave")
        {
            WalkRootChains(map, RootPorts.OnLeave, card.DefaultTarget, effects, errors, playAfterAnimation: false);
        }
        else if (map.Edges.Count > 0)
        {
            errors.Add("身材槽不得携带行为链");
        }

        if (effects.Count > 0) config.Effects = effects.ToArray();
        if (triggers.Count > 0) config.Triggers = triggers.ToArray();
        if (auras.Count > 0) config.Auras = auras.ToArray();
        return errors.Count == 0 ? config : null;
    }

    // ==================== 入场/离场链：目标切换开新组，分支=多组 ====================

    static void WalkRootChains(Map map, string port, string defaultTarget,
        List<EffectGroupDef> sink, List<string> errors, bool playAfterAnimation)
    {
        foreach (PartGraphNode head in map.ChainHeads(port))
            WalkSegment(map, head, ParseTargetMode(defaultTarget, "技能目标", errors), sink, errors, playAfterAnimation);
    }

    /// <summary>从 head 走一条段：同目标连续节点合并进一组；遇目标切换/扇出即断组递归。</summary>
    static void WalkSegment(Map map, PartGraphNode node, TargetMode current,
        List<EffectGroupDef> sink, List<string> errors, bool playAfterAnimation)
    {
        List<EffectOp> ops = new List<EffectOp>();
        while (node != null)
        {
            switch (node)
            {
                case TargetSwitchNodeData sw:
                    Flush();
                    current = ParseTargetMode(sw.TargetMode, $"选择目标节点[{map.ShortName(sw)}]", errors);
                    break;
                case OpNodeData op:
                    if (op.Kind == OpNodeKind.Condition)
                    {
                        EffectOp conditional = BuildConditional(map, op, errors);
                        if (conditional != null) ops.Add(conditional);
                    }
                    else
                    {
                        if (op.Target != NodeTargetScope.ParentPassed)
                        {
                            Flush(); // 节点目标三选一：切换=开新组
                            current = op.Target == NodeTargetScope.Caster ? TargetMode.Self : TargetMode.Chosen;
                        }
                        EffectOp built = BuildOp(map, op, errors);
                        if (built != null) ops.Add(built);
                    }
                    break;
                default:
                    errors.Add($"节点[{map.ShortName(node)}]不能出现在效果链上（触发配置节点仅常驻触发链首合法）");
                    break;
            }
            List<PartGraphNode> next = map.Successors(node);
            if (next.Count == 0) { node = null; break; }
            if (next.Count == 1) { node = next[0]; continue; }
            Flush(); // 并行分支：前缀断组，各分支独立成段，顺序=左→右（需求文档§4.2）
            foreach (PartGraphNode branch in next)
                WalkSegment(map, branch, current, sink, errors, playAfterAnimation);
            return;
        }
        Flush();
        return;

        void Flush()
        {
            if (ops.Count == 0) return;
            sink.Add(new EffectGroupDef { Target = current, Ops = ops.ToArray(), PlayAfterAnimation = playAfterAnimation });
            ops = new List<EffectOp>();
        }
    }

    // ==================== 常驻·触发链（链首触发配置 → TriggerDef，单组单目标） ====================

    static void WalkTriggerChains(Map map, List<TriggerDef> sink, List<string> errors)
    {
        foreach (PartGraphNode head in map.ChainHeads(RootPorts.ContinuousTrigger))
        {
            if (!(head is TriggerConfigNodeData cfg))
            {
                errors.Add($"常驻「触发」链首必须是触发配置节点（当前是[{map.ShortName(head)}]）");
                continue;
            }
            List<EffectOp> ops = new List<EffectOp>();
            CollectOps(map, map.Successors(head).FirstOrDefault(), ops, errors, singleGroup: true);
            if (ops.Count == 0) errors.Add($"触发配置[{cfg.EventTag}]后面没接任何操作");
            sink.Add(new TriggerDef
            {
                EventTag = cfg.EventTag,
                Timing = cfg.EventTag.EndsWith(".Preview", StringComparison.Ordinal)
                    ? TriggerTiming.Preview : TriggerTiming.After,
                SelfOnly = cfg.SelfOnly,
                SourceOnly = cfg.SourceOnly,
                Once = cfg.Once,
                ValueFilter = cfg.HasValueFilter
                    ? new EventValueCondition { Op = CompareOperator.GreaterOrEqual, Value = cfg.ValueFilterMin }
                    : null,
                Effects = new EffectGroupDef { Target = TargetMode.Self, Ops = ops.ToArray() },
            });
        }
    }

    // ==================== 常驻·光环链（属性修饰/赋予关键词 → 永久持续效果定义） ====================

    static void WalkAuraChains(Map map, List<GameplayEffectDef> sink, List<string> errors)
    {
        foreach (PartGraphNode head in map.ChainHeads(RootPorts.ContinuousAura))
        {
            List<AttributeModifierDef> modifiers = new List<AttributeModifierDef>();
            List<string> tags = new List<string>();
            void Walk(PartGraphNode node)
            {
                while (node != null)
                {
                    if (node is OpNodeData op && op.Kind == OpNodeKind.ModifyAttribute)
                        modifiers.Add(new AttributeModifierDef
                        {
                            Attribute = ParseAttribute(op.Attribute, map, op, errors),
                            Op = ParseModOp(op.ModOp, map, op, errors),
                            Magnitude = ScopedValue.Const(op.Magnitude),
                        });
                    else if (node is OpNodeData grant && grant.Kind == OpNodeKind.GrantTag && !string.IsNullOrEmpty(grant.Tag))
                        tags.Add(grant.Tag);
                    else
                        errors.Add($"光环链只接受「属性修饰/赋予关键词」节点（[{map.ShortName(node)}]非法）");
                    List<PartGraphNode> next = map.Successors(node);
                    foreach (PartGraphNode branch in next) Walk(branch);
                    node = null;
                }
            }
            Walk(head);
            if (modifiers.Count == 0 && tags.Count == 0)
                errors.Add("光环链没有任何属性修饰/赋予关键词");
            else
                sink.Add(new GameplayEffectDef
                {
                    Name = "光环",
                    Duration = EffectDurationKind.Permanent,
                    Modifiers = modifiers.Count > 0 ? modifiers.ToArray() : null,
                    GrantedTags = tags.Count > 0 ? tags.ToArray() : null,
                });
        }
    }

    // ==================== 条件分支（Then/Else=纯 op 子链） ====================

    static ConditionalOp BuildConditional(Map map, OpNodeData op, List<string> errors)
    {
        List<EffectOp> thenOps = new List<EffectOp>();
        List<EffectOp> elseOps = new List<EffectOp>();
        CollectOps(map, map.SuccessorsFrom(op.Guid + ":yes").FirstOrDefault(), thenOps, errors, singleGroup: false);
        CollectOps(map, map.SuccessorsFrom(op.Guid + ":no").FirstOrDefault(), elseOps, errors, singleGroup: false);
        return new ConditionalOp
        {
            Condition = new AttributeCompareCondition
            {
                Attribute = ParseAttribute(op.CondAttribute, map, op, errors),
                Op = ParseCompare(op.CondOp, map, op, errors),
                ValueType = ParseValueType(op.CondValueType, map, op, errors),
                Value = op.CondValue,
            },
            ConditionTarget = ParseConditionTarget(op.CondSubject, map, op, errors),
            Then = thenOps.ToArray(),
            Else = elseOps.ToArray(),
        };
    }

    /// <summary>纯 op 子链收集（条件分支内/触发链内）：不允许选择目标节点与节点目标切换（数据合同倒逼，双重校验）。</summary>
    static void CollectOps(Map map, PartGraphNode node, List<EffectOp> sink, List<string> errors, bool singleGroup)
    {
        while (node != null)
        {
            switch (node)
            {
                case TargetSwitchNodeData _:
                    errors.Add(singleGroup
                        ? "触发链内不许插选择目标节点（TriggerDef 只装一个效果组）"
                        : "条件分支内不许插选择目标节点（需求文档§4.4 当前版本限制）");
                    break;
                case OpNodeData op:
                    if (op.Target != NodeTargetScope.ParentPassed)
                        errors.Add(singleGroup
                            ? $"触发链内节点[{map.ShortName(op)}]不许切节点目标（单组单目标）"
                            : $"条件分支内节点[{map.ShortName(op)}]不许切节点目标（需求文档§4.4）");
                    if (op.Kind == OpNodeKind.Condition)
                    {
                        EffectOp conditional = BuildConditional(map, op, errors);
                        if (conditional != null) sink.Add(conditional);
                    }
                    else
                    {
                        EffectOp built = BuildOp(map, op, errors);
                        if (built != null) sink.Add(built);
                    }
                    break;
                default:
                    errors.Add($"节点[{map.ShortName(node)}]不能出现在此链上");
                    break;
            }
            List<PartGraphNode> next = map.Successors(node);
            if (next.Count <= 1) { node = next.Count == 1 ? next[0] : null; continue; }
            foreach (PartGraphNode branch in next) CollectOps(map, branch, sink, errors, singleGroup);
            return;
        }
    }

    // ==================== op 构造与解析 ====================

    static EffectOp BuildOp(Map map, OpNodeData op, List<string> errors)
    {
        switch (op.Kind)
        {
            case OpNodeKind.Damage: return new DamageOp { Amount = ScopedValue.Const(op.Amount) };
            case OpNodeKind.Heal: return new HealOp { Amount = ScopedValue.Const(op.Amount) };
            case OpNodeKind.DrawCards: return new DrawCardsOp { Count = Math.Max(1, op.Count) };
            case OpNodeKind.GrantTag:
                if (string.IsNullOrEmpty(op.Tag)) { errors.Add($"节点[{map.ShortName(op)}]GrantTag 未填标签"); return null; }
                return new GrantTagOp
                {
                    Tag = op.Tag,
                    Duration = ParseDuration(op.Duration, map, op, errors),
                    Rounds = Math.Max(1, op.Rounds),
                };
            case OpNodeKind.ModifyAttribute:
                return new ModifyAttributeOp
                {
                    Attribute = ParseAttribute(op.Attribute, map, op, errors),
                    Op = ParseModOp(op.ModOp, map, op, errors),
                    Magnitude = ScopedValue.Const(op.Magnitude),
                    Duration = ParseDuration(op.Duration, map, op, errors),
                    Rounds = Math.Max(1, op.Rounds),
                };
            default: return null;
        }
    }

    static SlotType ParseSlot(string s, List<string> errors) =>
        Enum.TryParse<SlotType>(s, out SlotType v) ? v : Error<SlotType>(errors, $"未知槽型 {s}");
    static T Error<T>(List<string> errors, string message)
    {
        errors.Add(message);
        return default;
    }

    static TargetMode ParseTargetMode(string s, string where, List<string> errors) =>
        Enum.TryParse<TargetMode>(s, out TargetMode v)
            ? v
            : Error<TargetMode>(errors, $"{where}：未知目标模式 {s}");

    static AttributeId ParseAttribute(string s, Map map, PartGraphNode n, List<string> errors) =>
        Enum.TryParse<AttributeId>(s, out AttributeId v)
            ? v
            : Error<AttributeId>(errors, $"节点[{map.ShortName(n)}]：未知属性 {s}");

    static ModOp ParseModOp(string s, Map map, PartGraphNode n, List<string> errors) =>
        Enum.TryParse<ModOp>(s, out ModOp v)
            ? v
            : Error<ModOp>(errors, $"节点[{map.ShortName(n)}]：未知修饰方式 {s}");

    static EffectDurationKind ParseDuration(string s, Map map, PartGraphNode n, List<string> errors) =>
        Enum.TryParse<EffectDurationKind>(s, out EffectDurationKind v)
            ? v
            : Error<EffectDurationKind>(errors, $"节点[{map.ShortName(n)}]：未知时长 {s}");

    static CompareOperator ParseCompare(string s, Map map, PartGraphNode n, List<string> errors) =>
        Enum.TryParse<CompareOperator>(s, out CompareOperator v)
            ? v
            : Error<CompareOperator>(errors, $"节点[{map.ShortName(n)}]：未知比较符 {s}");

    static CompareValueType ParseValueType(string s, Map map, PartGraphNode n, List<string> errors) =>
        Enum.TryParse<CompareValueType>(s, out CompareValueType v)
            ? v
            : Error<CompareValueType>(errors, $"节点[{map.ShortName(n)}]：未知比较值类型 {s}");

    static ConditionTargetKind ParseConditionTarget(string s, Map map, PartGraphNode n, List<string> errors) =>
        Enum.TryParse<ConditionTargetKind>(s, out ConditionTargetKind v)
            ? v
            : Error<ConditionTargetKind>(errors, $"节点[{map.ShortName(n)}]：未知求值对象 {s}");

    static string[] SplitTags(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return null;
        return csv.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToArray();
    }

    // ==================== 图索引 ====================

    sealed class Map
    {
        readonly Dictionary<string, PartGraphNode> byGuid = new Dictionary<string, PartGraphNode>();
        readonly Dictionary<string, List<string>> edgesFrom = new Dictionary<string, List<string>>();
        readonly HashSet<string> chainHeadKeys;

        public readonly List<PartGraphEdgeData> Edges = new List<PartGraphEdgeData>();

        public Map(PartCardAuthoringAsset card)
        {
            foreach (PartGraphNode node in card.Nodes) byGuid[node.Guid] = node;
            foreach (PartGraphEdgeData edge in card.Edges)
            {
                if (!byGuid.ContainsKey(edge.To)) continue; // 悬空边（节点已删）忽略
                Edges.Add(edge);
                if (!edgesFrom.TryGetValue(edge.From, out List<string> list))
                    edgesFrom[edge.From] = list = new List<string>();
                list.Add(edge.To);
            }
            chainHeadKeys = new HashSet<string>(RootPorts.Of(card.SlotType));
        }

        /// <summary>某出口的直连节点，按 X 坐标左→右（并行分支执行序——需求文档§4.2）。</summary>
        public List<PartGraphNode> SuccessorsFrom(string fromKey)
        {
            if (!edgesFrom.TryGetValue(fromKey, out List<string> tos)) return new List<PartGraphNode>();
            return tos.Select(t => byGuid.TryGetValue(t, out PartGraphNode n) ? n : null)
                .Where(n => n != null)
                .OrderBy(n => n.Position.x).ThenBy(n => n.Position.y).ToList();
        }

        public List<PartGraphNode> Successors(PartGraphNode node) => SuccessorsFrom(node.Guid);

        public List<PartGraphNode> ChainHeads(string port)
        {
            if (!chainHeadKeys.Contains(port)) return new List<PartGraphNode>();
            return SuccessorsFrom(port);
        }

        public string ShortName(PartGraphNode node) => node == null ? "∅" : node switch
        {
            OpNodeData op => op.Kind.ToString(),
            TargetSwitchNodeData ts => $"选目标→{ts.TargetMode}",
            TriggerConfigNodeData tc => $"触发:{tc.EventTag}",
            _ => node.GetType().Name,
        };
    }
}
