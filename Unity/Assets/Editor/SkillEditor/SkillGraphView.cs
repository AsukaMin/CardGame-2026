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
        SetupZoom(ContentZoomer.DefaultMinScale, 3f); // 最大 1.25 → 3（验收反馈：放大上限不够）；最小 0.25 不动
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
        view.style.height = StyleKeyword.Auto; // 参数行数不定，高度随内容（宽度仍 DefaultSize.x）
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

    readonly VisualElement paramList;  // 展开态：主要参数清单（端口行正下方）
    readonly Label summaryStrip;       // 收起态：黑底一行简述
    bool collapsed;                    // 自管收起态——不动 expanded（6000.3 无 Port.alwaysVisible，内置收起会藏未连接端口）

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
        // 先插简述条再插参数区 → 最终顺序：端口行 → paramList → summaryStrip
        summaryStrip = PartNodeStyles.NewSummaryStrip();
        paramList = PartNodeStyles.NewParamList();
        PartNodeStyles.InsertBelowPorts(this, summaryStrip);
        PartNodeStyles.InsertBelowPorts(this, paramList);
        RefreshSummary();
        ApplyCollapseState();
        RefreshExpandedState();
    }

    /// <summary>收起行为改造（2026-10-31 定案）：拦截内置折叠按钮、不调 base（expanded 恒 true，
    /// 内置"收起藏端口"不触发），只切换参数区/黑底简述条；折叠箭头指向靠 USS 类翻转。</summary>
    protected override void ToggleCollapse()
    {
        collapsed = !collapsed;
        EnableInClassList("collapsed", collapsed);
        EnableInClassList("expanded", !collapsed);
        ApplyCollapseState();
    }

    void ApplyCollapseState()
    {
        paramList.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
        summaryStrip.style.display = collapsed ? DisplayStyle.Flex : DisplayStyle.None;
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

    /// <summary>刷新节点显示：展开态=主要参数清单（每字段一行），收起态=单行简述。
    /// 只读——完整编辑在右栏（行内编辑 M6）；中文显示经 PartCardLabels，数据侧仍存英文枚举名。</summary>
    public void RefreshSummary()
    {
        paramList.Clear();
        string brief = "";
        switch (Data)
        {
            case OpNodeData op:
                switch (op.Kind)
                {
                    case OpNodeKind.Damage:
                        brief = $"{op.Amount} 点伤害";
                        paramList.AddParam("数值", op.Amount.ToString());
                        break;
                    case OpNodeKind.Heal:
                        brief = $"{op.Amount} 点治疗";
                        paramList.AddParam("数值", op.Amount.ToString());
                        break;
                    case OpNodeKind.ModifyAttribute:
                        brief = $"{PartCardLabels.Attribute(op.Attribute)} {PartCardLabels.Mod(op.ModOp)} {op.Magnitude}（{PartCardLabels.Duration(op.Duration)}）";
                        paramList.AddParam("属性", PartCardLabels.Attribute(op.Attribute));
                        paramList.AddParam("修饰方式", PartCardLabels.Mod(op.ModOp));
                        paramList.AddParam("数值", op.Magnitude.ToString());
                        paramList.AddParam("时长", PartCardLabels.Duration(op.Duration) + (op.Duration == "Rounds" ? $" ×{op.Rounds} 回合" : ""));
                        break;
                    case OpNodeKind.DrawCards:
                        brief = $"抽 {op.Count} 张";
                        paramList.AddParam("张数", op.Count.ToString());
                        break;
                    case OpNodeKind.GrantTag:
                        brief = op.Tag;
                        paramList.AddParam("标签", op.Tag);
                        paramList.AddParam("时长", PartCardLabels.Duration(op.Duration) + (op.Duration == "Rounds" ? $" ×{op.Rounds} 回合" : ""));
                        break;
                    case OpNodeKind.Condition:
                        brief = $"{PartCardLabels.Attribute(op.CondAttribute)} {PartCardLabels.Display(PartCardLabels.CompareValues, PartCardLabels.CompareNames, op.CondOp)} {op.CondValue}";
                        paramList.AddParam("属性", PartCardLabels.Attribute(op.CondAttribute));
                        paramList.AddParam("运算符", PartCardLabels.Display(PartCardLabels.CompareValues, PartCardLabels.CompareNames, op.CondOp));
                        paramList.AddParam("值", op.CondValue.ToString());
                        paramList.AddParam("求值对象", PartCardLabels.Display(PartCardLabels.SubjectValues, PartCardLabels.SubjectNames, op.CondSubject));
                        break;
                }
                if (op.Target != NodeTargetScope.ParentPassed)
                {
                    string target = op.Target == NodeTargetScope.Caster ? "施法者" : "技能主目标";
                    brief += $"  [目标:{target}]";
                    paramList.AddParam("节点目标", target);
                }
                break;
            case TargetSwitchNodeData ts:
                brief = PartCardLabels.Target(ts.TargetMode);
                paramList.AddParam("目标模式", PartCardLabels.Target(ts.TargetMode));
                break;
            case TriggerConfigNodeData tc:
                brief = PartCardLabels.EventTag(tc.EventTag)
                    + (tc.SelfOnly ? " ·自身受" : "") + (tc.SourceOnly ? " ·自身攻" : "") + (tc.Once ? " ·一次" : "");
                paramList.AddParam("事件", PartCardLabels.EventTag(tc.EventTag));
                List<string> limits = new List<string>();
                if (tc.SelfOnly) limits.Add("自身受");
                if (tc.SourceOnly) limits.Add("自身攻");
                if (tc.Once) limits.Add("一次");
                if (limits.Count > 0) paramList.AddParam("限定", string.Join(" / ", limits));
                if (tc.HasValueFilter) paramList.AddParam("数值过滤", $"≥ {tc.ValueFilterMin}");
                break;
        }
        summaryStrip.text = brief;
        title = TitleOf(Data);
    }
}

/// <summary>技能主节点（根）：卡级字段在右栏编辑；主体显示卡级参数清单 + 按槽型生成输出端口（收起同普通节点）。</summary>
public sealed class RootNodeView : Node
{
    public readonly PartCardAuthoringAsset Asset;
    public readonly Dictionary<string, Port> Ports = new Dictionary<string, Port>();
    readonly VisualElement paramList;
    readonly Label summaryStrip;
    bool collapsed;

    public RootNodeView(PartCardAuthoringAsset asset)
    {
        Asset = asset;
        title = "技能节点（卡）";
        summaryStrip = PartNodeStyles.NewSummaryStrip();
        paramList = PartNodeStyles.NewParamList();
        PartNodeStyles.InsertBelowPorts(this, summaryStrip);
        PartNodeStyles.InsertBelowPorts(this, paramList); // 先插简述条再插参数区 → 端口行 → paramList → summaryStrip
        RefreshPorts(asset);
        ApplyCollapseState();
    }

    /// <summary>收起行为同普通节点：拦截内置折叠（expanded 恒 true，端口与主体不被内置逻辑隐藏）。</summary>
    protected override void ToggleCollapse()
    {
        collapsed = !collapsed;
        EnableInClassList("collapsed", collapsed);
        EnableInClassList("expanded", !collapsed);
        ApplyCollapseState();
    }

    void ApplyCollapseState()
    {
        paramList.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
        summaryStrip.style.display = collapsed ? DisplayStyle.Flex : DisplayStyle.None;
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
        paramList.Clear();
        paramList.AddParam("卡牌 Id", asset.CardId.ToString());
        paramList.AddParam("费用", asset.Cost.ToString());
        paramList.AddParam("技能目标", PartCardLabels.Target(asset.DefaultTarget));
        paramList.AddParam("关键词", string.IsNullOrEmpty(asset.GrantTags) ? "无" : asset.GrantTags);
        summaryStrip.text = $"id {asset.CardId} · 费 {asset.Cost} · 目标 {PartCardLabels.Target(asset.DefaultTarget)} · 关键词 {(string.IsNullOrEmpty(asset.GrantTags) ? "无" : asset.GrantTags)}";
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

/// <summary>两个节点视图共用的主体样式与构造（参数：字号 12、浅灰字、简述条深底 rgb(56,56,56)——XH 同款取值）。
/// 注意 Unity 6 的 Node.mainContainer 实为 node-border 根容器：主体内容必须插进 contents（端口行的父级），
/// 直接 mainContainer.Add 会贴到节点最底部黑边（验收反馈两次"参数在最下面黑底"的根因）。</summary>
static class PartNodeStyles
{
    /// <summary>把元素插到端口行正下方（端口行 → 本元素 → 原有下方内容）。</summary>
    public static void InsertBelowPorts(Node node, VisualElement element)
    {
        VisualElement top = node.outputContainer.parent; // topContainer（input/output 两行所在）
        VisualElement contents = top.parent;
        contents.Insert(contents.IndexOf(top) + 1, element);
    }

    public static VisualElement NewParamList() => new VisualElement
    {
        name = "paramList",
        style = { marginTop = 4 },
    };

    public static Label NewSummaryStrip() => new Label
    {
        name = "summaryStrip",
        style =
        {
            fontSize = 12,
            color = new Color(0.85f, 0.85f, 0.85f, 1f),
            backgroundColor = new Color(56f / 255f, 56f / 255f, 56f / 255f, 1f),
            marginTop = 4,
            marginBottom = 2,
            paddingLeft = 4,
            paddingRight = 4,
            paddingTop = 2,
            paddingBottom = 2,
            whiteSpace = WhiteSpace.Normal,
        },
    };

    /// <summary>参数清单加一行"字段名: 值"。</summary>
    public static void AddParam(this VisualElement list, string name, string value)
    {
        list.Add(new Label($"{name}: {value}")
        {
            style =
            {
                fontSize = 12,
                color = new Color(0.8f, 0.8f, 0.8f, 1f),
                marginTop = 1,
                whiteSpace = WhiteSpace.Normal,
            },
        });
    }
}
