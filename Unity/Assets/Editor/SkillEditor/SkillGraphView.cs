using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 技能编辑器画布（GraphView，定案 2026-10-28）：缩放/拖动/框选/全览原生；
/// 连线规则在创建时强制——输入单进、禁自环禁成环、常驻双端口白名单（触发链首=触发配置节点、光环链只收属性修饰/赋予关键词）。
/// 图数据同步：节点增删/移动/连线变化即时写回 PartCardAuthoringAsset（脏标由窗口管理）。
/// 节点上的参数行为只读摘要（完整编辑在右栏参数面板）——M5 简化，行内编辑 M6。
/// </summary>
public sealed class SkillGraphView : GraphView
{
    public PartCardAuthoringAsset Asset { get; private set; }
    public Action<GraphElement> OnSelectionChanged;
    public Action<string> OnGraphDirty; // 参数=操作名（撤销提交用——快照在窗口侧统一落）

    RootNodeView root;
    readonly Dictionary<string, PartNodeView> nodeViews = new Dictionary<string, PartNodeView>();

    public SkillGraphView()
    {
        SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());
        this.AddManipulator(new ContextualMenuManipulator(BuildNodeMenu));
        Insert(0, new GridBackground { name = "grid" });
        style.flexGrow = 1;
        graphViewChanged += OnGraphViewChanged;
        this.Q<GridBackground>()?.SendToBack();
    }

    GraphElement lastSelection;

    /// <summary>选择同步（轮询驱动——Unity 6 GraphView 无 selectionChanged 事件，窗口在 update 里调）。</summary>
    public void PollSelection()
    {
        GraphElement current = selection.OfType<GraphElement>().FirstOrDefault();
        if (current == lastSelection) return;
        lastSelection = current;
        OnSelectionChanged?.Invoke(current);
    }

    // ==================== 载入卡（图 ← 作者稿数据） ====================

    public void Load(PartCardAuthoringAsset asset)
    {
        Asset = asset;
        ClearGraph();
        if (asset == null) return;

        root = new RootNodeView(asset);
        AddElement(root);

        foreach (PartGraphNode data in asset.Nodes)
        {
            PartNodeView view = CreateView(data);
            nodeViews[data.Guid] = view;
            AddElement(view);
        }
        foreach (PartGraphEdgeData edgeData in asset.Edges)
            Connect(edgeData.From, edgeData.To);
        FrameAll();
    }

    void ClearGraph()
    {
        nodeViews.Clear();
        foreach (GraphElement element in graphElements.ToList()) RemoveElement(element);
        root = null;
    }

    PartNodeView CreateView(PartGraphNode data)
    {
        PartNodeView view = new PartNodeView(data);
        view.SetPosition(new Rect(data.Position, PartNodeView.DefaultSize));
        view.RefreshSummary();
        return view;
    }

    void Connect(string fromKey, string toGuid)
    {
        Port output = FindOutputPort(fromKey);
        if (!nodeViews.TryGetValue(toGuid, out PartNodeView target)) return;
        Port input = target.Input;
        if (output == null || input == null || input.connected) return;
        AddElement(new Edge { output = output, input = input });
    }

    Port FindOutputPort(string fromKey)
    {
        if (root != null && root.Ports.TryGetValue(fromKey, out Port rootPort)) return rootPort;
        int colon = fromKey.LastIndexOf(':');
        if (colon > 0 && nodeViews.TryGetValue(fromKey.Substring(0, colon), out PartNodeView source))
            return fromKey.Substring(colon + 1) == "no" ? source.OutputNo : source.OutputYes ?? source.Output;
        return null;
    }

    // ==================== 右键菜单：创建节点（需求文档§4.1 分层菜单） ====================

    void BuildNodeMenu(ContextualMenuPopulateEvent evt)
    {
        Vector2 pos = contentViewContainer.WorldToLocal(evt.mousePosition); // 池化事件须在分发期间取值，回调延迟读 evt 会拿到已回收的 (0,0)
        evt.menu.AppendAction("创建节点/操作节点/伤害", _ => AddNodeAt(pos, new OpNodeData { Kind = OpNodeKind.Damage, Amount = 2 }));
        evt.menu.AppendAction("创建节点/操作节点/治疗", _ => AddNodeAt(pos, new OpNodeData { Kind = OpNodeKind.Heal, Amount = 2 }));
        evt.menu.AppendAction("创建节点/操作节点/属性修饰", _ => AddNodeAt(pos, new OpNodeData { Kind = OpNodeKind.ModifyAttribute, Attribute = "Attack" }));
        evt.menu.AppendAction("创建节点/操作节点/抽牌", _ => AddNodeAt(pos, new OpNodeData { Kind = OpNodeKind.DrawCards, Count = 1 }));
        evt.menu.AppendAction("创建节点/操作节点/赋予关键词", _ => AddNodeAt(pos, new OpNodeData { Kind = OpNodeKind.GrantTag, Tag = "Keyword.Taunt" }));
        evt.menu.AppendAction("创建节点/操作节点/条件分支", _ => AddNodeAt(pos, new OpNodeData { Kind = OpNodeKind.Condition, CondValue = 0 }));
        evt.menu.AppendAction("创建节点/选择目标节点", _ => AddNodeAt(pos, new TargetSwitchNodeData()));
        evt.menu.AppendAction("创建节点/触发配置节点（常驻·触发链首）", _ => AddNodeAt(pos, new TriggerConfigNodeData()));
        evt.menu.AppendSeparator();
        evt.menu.AppendAction("全览", _ => FrameAll());
    }

    void AddNodeAt(Vector2 position, PartGraphNode data)
    {
        data.Position = position;
        Asset.Nodes.Add(data);
        PartNodeView view = CreateView(data);
        nodeViews[data.Guid] = view;
        AddElement(view);
        OnGraphDirty?.Invoke("创建节点");
    }

    /// <summary>删除选中节点（连边一并清；技能主节点不可删）。</summary>
    public void DeleteSelectedNodes()
    {
        foreach (GraphElement element in selection.OfType<GraphElement>().ToList())
        {
            if (!(element is PartNodeView view) || view.Data == null) continue;
            Asset.Nodes.Remove(view.Data);
            Asset.Edges.RemoveAll(e => e.From == view.Data.Guid || e.From.StartsWith(view.Data.Guid + ":", StringComparison.Ordinal) || e.To == view.Data.Guid);
            nodeViews.Remove(view.Data.Guid);
            RemoveElement(view);
        }
        OnGraphDirty?.Invoke("删除节点");
    }

    public void RefreshAllSummaries()
    {
        foreach (KeyValuePair<string, PartNodeView> pair in nodeViews) pair.Value.RefreshSummary();
        root?.RefreshPorts(Asset);
    }

    public void SelectRoot()
    {
        ClearSelection();
        if (root != null) AddToSelection(root);
        lastSelection = root;
        OnSelectionChanged?.Invoke(root);
    }

    /// <summary>撤销/重做后从资产重建画布：Unity 恢复的是序列化状态，[SerializeReference] 会换 POCO 实例，
    /// 旧视图的 Data 引用已失效必须全量重建；视口保持不动，选中按 Guid 尽量恢复。</summary>
    public void ReloadFromAsset()
    {
        if (Asset == null) return;
        string selectedGuid = (selection.OfType<PartNodeView>().FirstOrDefault()?.Data)?.Guid;
        Vector3 pan = contentViewContainer.transform.position, zoom = contentViewContainer.transform.scale;
        Load(Asset); // 内含 FrameAll，重建后还原视口撤销不跳视图（Unity 6 viewTransform 只读，直接写 content 容器）
        contentViewContainer.transform.position = pan;
        contentViewContainer.transform.scale = zoom;
        if (selectedGuid != null && nodeViews.TryGetValue(selectedGuid, out PartNodeView restored))
        {
            ClearSelection();
            AddToSelection(restored);
            lastSelection = restored;
            OnSelectionChanged?.Invoke(restored);
        }
        else SelectRoot();
    }

    // ==================== 连线规则（创建时强制） ====================

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
    {
        // Unity 6 的 graphElements 源码级排除 Port（GraphView.cs:288 Where(e => !(e is Port))）——
        // 端口必须 Query 直查；M5 曾用 graphElements.OfType<Port>() 恒得空列表，拖线时全端口禁用、无处可放
        return this.Query<Port>().ToList()
            .Where(p => p.direction != startPort.direction && p.node != startPort.node)
            .Where(p => p.direction != Direction.Input || !p.connected) // 输入单进
            .Where(p => !(p.node is RootNodeView))                       // 根无输入
            .ToList();
    }

    GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        if (Asset == null) return change;

        if (change.edgesToCreate != null)
        {
            List<Edge> allowed = new List<Edge>();
            List<PartGraphEdgeData> pending = new List<PartGraphEdgeData>();
            foreach (Edge edge in change.edgesToCreate)
            {
                if (ValidateEdge(edge, out string fromKey, out string error))
                {
                    allowed.Add(edge);
                    pending.Add(new PartGraphEdgeData { From = fromKey, To = ((PartNodeView)edge.input.node).Data.Guid });
                }
                else
                {
                    Debug.LogWarning($"[技能编辑器] 连线被拒：{error}");
                }
            }
            if (pending.Count > 0) // 先校验后提交，被拒的连线不产生空撤销步
            {
                Asset.Edges.AddRange(pending);
                OnGraphDirty?.Invoke("连线");
            }
            change.edgesToCreate = allowed;
        }

        if (change.elementsToRemove != null) // 删边（Delete 键/右键）与删节点一并在此（Unity 6 无 edgesToRemove 字段）
        {
            foreach (GraphElement element in change.elementsToRemove)
            {
                switch (element)
                {
                    case Edge edge when edge.input?.node is PartNodeView target:
                        Asset.Edges.RemoveAll(e => e.To == target.Data.Guid);
                        break;
                    case PartNodeView view when view.Data != null:
                    {
                        string guid = view.Data.Guid;
                        Asset.Nodes.Remove(view.Data);
                        Asset.Edges.RemoveAll(e =>
                            e.To == guid || e.From == guid || e.From.StartsWith(guid + ":", StringComparison.Ordinal));
                        nodeViews.Remove(guid);
                        break;
                    }
                }
            }
            if (change.elementsToRemove.Count > 0) OnGraphDirty?.Invoke("删除节点/边");
        }

        if (change.movedElements != null)
        {
            foreach (GraphElement element in change.movedElements)
                if (element is PartNodeView view && view.Data != null)
                    view.Data.Position = element.GetPosition().position;
            if (change.movedElements.Count > 0) OnGraphDirty?.Invoke("移动节点");
        }
        return change;
    }

    bool ValidateEdge(Edge edge, out string fromKey, out string error)
    {
        error = null;
        Port output = edge.output, input = edge.input;
        fromKey = PortKeyOf(output);

        if (!(input?.node is PartNodeView targetView) || targetView.Data == null)
        {
            error = "目标不是普通节点";
            return false;
        }
        if (CreatesCycle(output, targetView))
        {
            error = "禁止连成环";
            return false;
        }

        // 常驻双端口白名单（技术设计§4.1/定案 2）
        if (fromKey == RootPorts.ContinuousTrigger)
        {
            if (!(targetView.Data is TriggerConfigNodeData))
            {
                error = "常驻「触发」链首必须是触发配置节点";
                return false;
            }
        }
        else if (fromKey == RootPorts.ContinuousAura)
        {
            bool legal = targetView.Data is OpNodeData op &&
                         (op.Kind == OpNodeKind.ModifyAttribute || op.Kind == OpNodeKind.GrantTag);
            if (!legal)
            {
                error = "光环链只接受「属性修饰 / 赋予关键词」节点";
                return false;
            }
        }
        else if (targetView.Data is TriggerConfigNodeData)
        {
            error = "触发配置节点只能接在常驻「触发」端口后";
            return false;
        }
        return true;
    }

    string PortKeyOf(Port port)
    {
        if (port.node is RootNodeView) return port.viewDataKey; // 根端口键=root:…
        if (!(port.node is PartNodeView view)) return "";
        if (port == view.OutputNo) return view.Data.Guid + ":no";
        if (port == view.OutputYes) return view.Data.Guid + ":yes";
        return view.Data.Guid;
    }

    bool CreatesCycle(Port output, PartNodeView target)
    {
        // 从目标节点出发沿输出向下走，若能回到输出方节点 → 成环
        string sourceGuid = (output.node as PartNodeView)?.Data?.Guid;
        HashSet<string> visited = new HashSet<string>();
        bool Reachable(PartNodeView from)
        {
            if (from == null || !visited.Add(from.Data.Guid)) return false;
            if (from.Data.Guid == sourceGuid) return true;
            return @from.Output.connections.Any(c =>
                Reachable(c.input?.node as PartNodeView)) ||
                   (@from.OutputYes?.connections.Any(c => Reachable(c.input?.node as PartNodeView)) ?? false) ||
                   (@from.OutputNo?.connections.Any(c => Reachable(c.input?.node as PartNodeView)) ?? false);
        }
        return Reachable(target);
    }
}

// ==================== 节点视图 ====================

/// <summary>普通节点视图（操作/选择目标/触发配置通用壳：输入单进 + 输出多连；条件分支加是/否双出口）。</summary>
public sealed class PartNodeView : Node
{
    public static readonly Vector2 DefaultSize = new Vector2(210, 90);

    public readonly PartGraphNode Data;
    public readonly Port Input;
    public readonly Port Output;
    public readonly Port OutputYes; // 条件分支专用（可空）
    public readonly Port OutputNo;

    readonly Label summary;

    public PartNodeView(PartGraphNode data)
    {
        Data = data;
        Input = Port.Create<Edge>(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(bool));
        Output = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
        Input.portName = "in";
        inputContainer.Add(Input);

        switch (data)
        {
            case OpNodeData op when op.Kind == OpNodeKind.Condition:
                title = "条件分支";
                OutputYes = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                OutputNo = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                OutputYes.portName = "是";
                OutputNo.portName = "否";
                outputContainer.Add(OutputYes);
                outputContainer.Add(OutputNo);
                break;
            default:
                title = TitleOf(data);
                Output.portName = "out";
                outputContainer.Add(Output);
                break;
        }
        // 摘要放节点主体（与端口同区灰底）——XH 的 DamageEffectNode 同款；不进 extensionContainer（深色块、字小，验收反馈）
        summary = new Label
        {
            name = "summary",
            style =
            {
                fontSize = 12,                             // XH 节点内显式字号同款（12）
                color = new Color(0.8f, 0.8f, 0.8f, 1f),   // XH 摘要同款浅灰
                whiteSpace = WhiteSpace.Normal,            // 长摘要换行不溢出
                marginTop = 4,
            },
        };
        mainContainer.Add(summary);
        RefreshSummary();
        RefreshExpandedState();
    }

    public static string TitleOf(PartGraphNode data) => data switch
    {
        OpNodeData op => OpTitle(op.Kind),
        TargetSwitchNodeData ts => "选择目标",
        TriggerConfigNodeData _ => "触发配置",
        _ => data.GetType().Name,
    };

    static string OpTitle(OpNodeKind kind) => kind switch
    {
        OpNodeKind.Damage => "伤害",
        OpNodeKind.Heal => "治疗",
        OpNodeKind.ModifyAttribute => "属性修饰",
        OpNodeKind.DrawCards => "抽牌",
        OpNodeKind.GrantTag => "赋予关键词",
        OpNodeKind.Condition => "条件分支",
        _ => kind.ToString(),
    };

    /// <summary>节点参数摘要（只读——完整编辑在右栏；M5 简化。中文显示经 PartCardLabels，数据侧仍存英文枚举名）。</summary>
    public void RefreshSummary()
    {
        switch (Data)
        {
            case OpNodeData op:
                summary.text = op.Kind switch
                {
                    OpNodeKind.Damage => $"{op.Amount} 点伤害",
                    OpNodeKind.Heal => $"{op.Amount} 点治疗",
                    OpNodeKind.ModifyAttribute => $"{PartCardLabels.Attribute(op.Attribute)} {PartCardLabels.Mod(op.ModOp)} {op.Magnitude}（{PartCardLabels.Duration(op.Duration)}）",
                    OpNodeKind.DrawCards => $"抽 {op.Count} 张",
                    OpNodeKind.GrantTag => op.Tag,
                    OpNodeKind.Condition => $"{PartCardLabels.Attribute(op.CondAttribute)} {PartCardLabels.Display(PartCardLabels.CompareValues, PartCardLabels.CompareNames, op.CondOp)} {op.CondValue}",
                    _ => "",
                };
                if (op.Target != NodeTargetScope.ParentPassed)
                    summary.text += $"  [目标:{(op.Target == NodeTargetScope.Caster ? "施法者" : "技能主目标")}]";
                break;
            case TargetSwitchNodeData ts:
                summary.text = PartCardLabels.Target(ts.TargetMode);
                break;
            case TriggerConfigNodeData tc:
                summary.text = PartCardLabels.EventTag(tc.EventTag)
                    + (tc.SelfOnly ? " ·自身受" : "") + (tc.SourceOnly ? " ·自身攻" : "") + (tc.Once ? " ·一次" : "");
                break;
        }
        title = TitleOf(Data);
    }
}

/// <summary>技能主节点（根）：卡级字段在右栏编辑；此处显示摘要 + 按槽型生成输出端口。</summary>
public sealed class RootNodeView : Node
{
    public readonly PartCardAuthoringAsset Asset;
    public readonly Dictionary<string, Port> Ports = new Dictionary<string, Port>();
    Label summary;

    public RootNodeView(PartCardAuthoringAsset asset)
    {
        Asset = asset;
        title = "技能节点（卡）";
        summary = new Label
        {
            style =
            {
                fontSize = 12,
                color = new Color(0.8f, 0.8f, 0.8f, 1f),
                whiteSpace = WhiteSpace.Normal,
                marginTop = 4,
            },
        };
        mainContainer.Add(summary);
        RefreshPorts(asset);
    }

    public void RefreshPorts(PartCardAuthoringAsset asset)
    {
        foreach (Port port in Ports.Values.ToList())
        {
            outputContainer.Remove(port);
            port.DisconnectAll();
        }
        Ports.Clear();
        foreach (string key in RootPorts.Of(asset.SlotType))
        {
            Port port = Port.Create<Edge>(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            port.portName = DisplayName(key);
            port.viewDataKey = key; // 连线数据里的 From 键
            outputContainer.Add(port);
            Ports[key] = port;
        }
        summary.text = $"id {asset.CardId} · 费 {asset.Cost} · 目标 {PartCardLabels.Target(asset.DefaultTarget)} · 关键词 {(string.IsNullOrEmpty(asset.GrantTags) ? "无" : asset.GrantTags)}";
        RefreshExpandedState();
    }

    static string DisplayName(string key) => key switch
    {
        RootPorts.OnSummon => "入场·触发",
        RootPorts.OnSummonLate => "入场·动画完成时",
        RootPorts.ContinuousTrigger => "常驻·触发",
        RootPorts.ContinuousAura => "常驻·光环",
        RootPorts.OnLeave => "离场·触发",
        _ => key,
    };
}
