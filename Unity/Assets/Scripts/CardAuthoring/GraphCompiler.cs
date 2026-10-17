using System.Collections.Generic;
using Battle;

namespace CardAuthoring
{
    /// <summary>
    /// 图 → 效果数据编译器（纯 C#，零 UnityEngine，headless 可测）。
    /// 对每个效果组节点沿"操作/继续"边收集操作序列；遇 ConditionalOp 递归"是/否"分支填 Then/Else。
    /// 编译器只读数据不变更；环、菱形引用、链超长均以 warning 兜底（编辑器在连线时已前置拦截）。
    /// </summary>
    public static class GraphCompiler
    {
        /// <summary>编译产物：效果列表 + 孤立/异常警告。</summary>
        public class Result
        {
            public List<EffectDef> Effects = new List<EffectDef>();
            public List<string> Warnings = new List<string>();
        }

        public static Result Compile(
            IReadOnlyList<EffectDefNodeData> effectDefs,
            IReadOnlyList<OpNodeData> opNodes,
            IReadOnlyList<ConnectionData> connections)
        {
            Result result = new Result();

            Dictionary<string, OpNodeData> byGuid = new Dictionary<string, OpNodeData>();
            if (opNodes != null)
            {
                foreach (OpNodeData n in opNodes)
                {
                    if (byGuid.ContainsKey(n.guid))
                        result.Warnings.Add($"节点 guid 重复，后者覆盖前者：{n.guid}");
                    byGuid[n.guid] = n;
                }
            }

            Dictionary<string, List<OpNodeData>> edges =
                new Dictionary<string, List<OpNodeData>>();
            if (connections != null)
            {
                foreach (ConnectionData c in connections)
                {
                    if (string.IsNullOrEmpty(c?.outputNodeGuid) || string.IsNullOrEmpty(c.inputNodeGuid)) continue;
                    // 输入端必须是操作节点；输出端可以是效果组根（不在 byGuid 中，边键照样可用）
                    if (!byGuid.TryGetValue(c.inputNodeGuid, out OpNodeData to)) continue;
                    string key = EdgeKey(c.outputNodeGuid, c.outputPort);
                    if (!edges.TryGetValue(key, out List<OpNodeData> list))
                        edges[key] = list = new List<OpNodeData>();
                    list.Add(to);
                }
            }

            HashSet<string> reachable = new HashSet<string>();
            if (effectDefs != null)
            {
                foreach (EffectDefNodeData def in effectDefs)
                {
                    EffectDef effect = new EffectDef { Target = def.target };
                    effect.Ops = CollectChain(byGuid, edges, def.guid, PortNames.Ops, result, reachable, 0);
                    result.Effects.Add(effect);
                }
            }

            if (opNodes != null)
            {
                foreach (OpNodeData n in opNodes)
                {
                    if (reachable.Contains(n.guid)) continue;
                    string opName = n.GetOp()?.GetType().Name ?? "空节点";
                    result.Warnings.Add($"孤立操作节点（未接入任何效果组链，导出时跳过）：{opName}");
                }
            }
            return result;
        }

        static EffectOp[] CollectChain(
            Dictionary<string, OpNodeData> byGuid,
            Dictionary<string, List<OpNodeData>> edges,
            string fromGuid, string fromPort,
            Result result, HashSet<string> reachable, int depth)
        {
            if (depth > 32)
            {
                result.Warnings.Add("条件分支嵌套超过 32 层，已截断");
                return System.Array.Empty<EffectOp>();
            }

            List<EffectOp> ops = new List<EffectOp>();
            OpNodeData current = FirstTarget(edges, fromGuid, fromPort);
            int guard = 0;
            while (current != null)
            {
                if (++guard > 256)
                {
                    result.Warnings.Add("操作链超过 256 个节点（疑似环），已截断");
                    break;
                }
                if (!reachable.Add(current.guid))
                {
                    result.Warnings.Add("检测到环或节点被多处引用，已跳过重复段（效果树中每个操作只能属于一个位置）");
                    break;
                }

                EffectOp op = current.GetOp();
                if (op == null)
                {
                    result.Warnings.Add("空操作节点被跳过");
                }
                else if (op is ConditionalOp cond)
                {
                    cond.Then = CollectChain(byGuid, edges, current.guid, PortNames.Then, result, reachable, depth + 1);
                    cond.Else = CollectChain(byGuid, edges, current.guid, PortNames.Else, result, reachable, depth + 1);
                    ops.Add(cond);
                }
                else
                {
                    ops.Add(op);
                }
                current = FirstTarget(edges, current.guid, PortNames.Next);
            }
            return ops.Count > 0 ? ops.ToArray() : System.Array.Empty<EffectOp>();
        }

        static OpNodeData FirstTarget(Dictionary<string, List<OpNodeData>> edges, string guid, string port)
        {
            if (!edges.TryGetValue(EdgeKey(guid, port), out List<OpNodeData> list) || list.Count == 0)
                return null;
            return list[0]; // 端口容量为单出，多连线属非法数据，取第一条
        }

        static string EdgeKey(string guid, string port) => guid + "|" + (port ?? "");
    }
}
