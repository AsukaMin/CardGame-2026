using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardAuthoring.Editor
{
    /// <summary>
    /// 效果图画布：仿参考项目用 GraphView（缩放/框选/拖拽/右键建节点）。
    /// 连线被约束成合法效果树：输入单进、输出单出、禁止成环；效果组只能作为根。
    /// 数据流向：LoadGraph(数据→图) / SyncBackData(图→数据，保存与导出前调用)。
    /// </summary>
    public class EffectGraphView : GraphView
    {
        readonly Action onDirty;

        public EffectGraphView(Action dirtyCallback)
        {
            onDirty = dirtyCallback;

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ContentZoomer { minScale = 0.2f, maxScale = 3f });

            GridBackground grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();
            style.flexGrow = 1;
            style.backgroundColor = new Color(0.13f, 0.15f, 0.17f);

            RegisterCallback<ContextualMenuPopulateEvent>(BuildContextMenu);
            graphViewChanged += OnGraphChanged;

            // 选择通知走 PointerDown（UIToolkit 基石 API，规避各版本 GraphView 事件名差异）：
            // 左键点中节点 → NodePicked(节点)；左键点空白/背景 → NodePicked(null)
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                VisualElement target = evt.target as VisualElement;
                bool onNode = target != null && target.GetFirstAncestorOfType<Node>() != null;
                if (!onNode) NodePicked?.Invoke(null);
            });
        }

        /// <summary>左键点中节点（点空白处传 null）——窗口用它驱动右侧节点 Inspector。</summary>
        public event Action<GraphElement> NodePicked;

        void HookNodePointer(GraphElement element)
        {
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) NodePicked?.Invoke(element);
            });
        }

        // ---------- 菜单 ----------

        void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 mouse = evt.localMousePosition;
            evt.menu.AppendAction("创建/效果组", _ => AddNode(new EffectDefNode(
                new EffectDefNodeData { x = mouse.x - 80, y = mouse.y - 40 }, onDirty)));

            foreach (OpNodeDescriptor descriptor in OpNodeRegistry.All)
            {
                OpNodeDescriptor d = descriptor;
                evt.menu.AppendAction($"创建/{d.MenuPath}", _ =>
                {
                    OpNodeData data = new OpNodeData { x = mouse.x - 100, y = mouse.y - 60 };
                    data.SetOp(OpNodeRegistry.CreateOp(d.OpType));
                    AddNode(new OpNode(data, onDirty));
                });
            }
        }

        void AddNode(GraphElement node)
        {
            HookNodePointer(node);
            AddElement(node);
            onDirty?.Invoke();
        }

        // ---------- 连线规则 ----------

        public override List<Port> GetCompatiblePorts(Port start, NodeAdapter nodeAdapter)
        {
            return ports.ToList()
                .Where(p => p.node != start.node && p.direction != start.direction)
                .ToList();
        }

        GraphViewChange OnGraphChanged(GraphViewChange change)
        {
            // 非法连线在创建前过滤掉（返回值机制）
            if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
                change.edgesToCreate = change.edgesToCreate.Where(ValidateEdge).ToList();

            bool touched =
                (change.edgesToCreate != null && change.edgesToCreate.Count > 0) ||
                (change.elementsToRemove != null && change.elementsToRemove.Count > 0) ||
 (change.movedElements != null && change.movedElements.Count > 0);
            if (touched) onDirty?.Invoke();
            return change;
        }

        bool ValidateEdge(Edge edge)
        {
            if (edge?.output == null || edge?.input == null) return false;
            if (!(edge.input.node is OpNode)) return false;                 // 输入端只能是操作节点
            if (!(edge.output.node is EffectDefNode || edge.output.node is OpNode)) return false;
            if (edge.input.node == edge.output.node) return false;          // 不允许自环

            // 单进/单出（pending 边尚未连接，connected 只统计已建立的边）
            if (edge.output.connected || edge.input.connected) return false;

            // 成环检测：从输入端沿现有出边走，能回到输出端即成环
            HashSet<Node> visited = new HashSet<Node>();
            if (Reachable(edge.input.node, edge.output.node, visited))
            {
                Debug.LogWarning("[技能编辑器] 该连线会构成环——效果树不允许环（每个操作只能属于一个位置）");
                return false;
            }
            return true;
        }

        bool Reachable(Node from, Node target, HashSet<Node> visited)
        {
            if (from == target) return true;
            if (!visited.Add(from)) return false;
            foreach (Edge e in edges)
            {
                if (e.output?.node != from) continue;
                if (Reachable(e.input?.node, target, visited)) return true;
            }
            return false;
        }

        // ---------- 图 ↔ 数据 ----------

        /// <summary>数据 → 图（打开资产/切换档位时）。</summary>
        public void LoadGraph(
            IReadOnlyList<EffectDefNodeData> effectDefs,
            IReadOnlyList<OpNodeData> opNodes,
            IReadOnlyList<ConnectionData> connections)
        {
            ClearGraph();
            if (effectDefs == null && opNodes == null) return;

            var defNodes = new Dictionary<string, EffectDefNode>();
            var opNodesByGuid = new Dictionary<string, OpNode>();

            if (effectDefs != null)
                foreach (EffectDefNodeData d in effectDefs)
                {
                    var node = new EffectDefNode(d, onDirty);
                    defNodes[d.guid] = node;
                    HookNodePointer(node);
                    AddElement(node);
                }
            if (opNodes != null)
                foreach (OpNodeData n in opNodes)
                {
                    var node = new OpNode(n, onDirty);
                    opNodesByGuid[n.guid] = node;
                    HookNodePointer(node);
                    AddElement(node);
                }
            if (connections != null)
                foreach (ConnectionData c in connections)
                {
                    if (c == null || string.IsNullOrEmpty(c.inputNodeGuid)) continue;
                    if (!opNodesByGuid.TryGetValue(c.inputNodeGuid, out OpNode toNode)) continue;

                    Node fromNode = null;
                    if (!string.IsNullOrEmpty(c.outputNodeGuid))
                    {
                        if (defNodes.TryGetValue(c.outputNodeGuid, out EffectDefNode defNode)) fromNode = defNode;
                        else if (opNodesByGuid.TryGetValue(c.outputNodeGuid, out OpNode opFrom)) fromNode = opFrom;
                    }
                    if (fromNode == null) continue;

                    Port outputPort = FindOutputPort(fromNode, c.outputPort);
                    Port inputPort = toNode.inputContainer.Query<Port>().ToList().FirstOrDefault();
                    if (outputPort == null || inputPort == null) continue;

                    var edge = new Edge { output = outputPort, input = inputPort };
                    AddElement(edge);
                }
        }

        void ClearGraph()
        {
            List<GraphElement> toRemove = graphElements.ToList();
            foreach (GraphElement e in toRemove) RemoveElement(e);
        }

        static Port FindOutputPort(Node node, string portName)
        {
            foreach (Port p in node.outputContainer.Query<Port>().ToList())
                if ((string)p.userData == portName) return p;
            return null;
        }

        /// <summary>
        /// 图 → 数据（保存/导出前调用）：全量重建三个列表，增删节点自动正确。
        /// 效果组顺序按画布 x 坐标升序（图左 = 先执行）。
        /// </summary>
        public void SyncBackData(
            List<EffectDefNodeData> effectDefs,
            List<OpNodeData> opNodes,
            List<ConnectionData> connections)
        {
            var defList = graphElements.OfType<EffectDefNode>().ToList();
            var opList = graphElements.OfType<OpNode>().ToList();

            // 位置回写
            foreach (EffectDefNode n in defList)
            {
                Rect pos = n.GetPosition();
                n.Data.x = pos.x;
                n.Data.y = pos.y;
            }
            foreach (OpNode n in opList)
            {
                Rect pos = n.GetPosition();
                n.Data.x = pos.x;
                n.Data.y = pos.y;
            }

            defList.Sort((a, b) => a.Data.x.CompareTo(b.Data.x)); // 图左 = 先执行
            effectDefs.Clear();
            foreach (EffectDefNode n in defList) effectDefs.Add(n.Data);

            opNodes.Clear();
            foreach (OpNode n in opList) opNodes.Add(n.Data);

            connections.Clear();
            foreach (Edge e in edges)
            {
                if (e.output?.node == null || e.input?.node == null) continue;
                string fromGuid =
                    e.output.node is EffectDefNode defNode ? defNode.Data.guid
                    : e.output.node is OpNode opFrom ? opFrom.Data.guid
                    : null;
                if (fromGuid == null || !(e.input.node is OpNode toNode)) continue;
                connections.Add(new ConnectionData
                {
                    outputNodeGuid = fromGuid,
                    outputPort = e.output.userData as string,
                    inputNodeGuid = toNode.Data.guid,
                });
            }
        }
    }
}
