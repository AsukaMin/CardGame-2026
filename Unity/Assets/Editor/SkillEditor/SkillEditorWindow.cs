using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Battle;
using Fusion;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 技能编辑器主窗口（M5·T5' 部位卡版）：三栏——左=卡池（文件夹树+右键管理），中=GraphView 画布，右=参数面板。
/// 作者稿=每卡一个 PartCardAuthoringAsset（文件名=卡名，Assets/Editor/SkillCards/Parts/ 下，定案 2026-10-28）；
/// 导出=全库编译 → GameData/parts.json（重复 id/编译错误中止并列出）；导入=反编译建新卡（已有 id 跳过）。
/// 保存时机：切卡前、关窗时自动；工具栏按钮与 Ctrl+S（Unity 全局保存）显式。
/// </summary>
public sealed class SkillEditorWindow : EditorWindow
{
    const string PartsRoot = "Assets/Editor/SkillCards/Parts";

    static SkillEditorWindow instance;

    SkillGraphView graph;
    PartCardTreeView treeView;
    IMGUIContainer toolbarView, poolView, propertyView;
    GraphElement selection;

    PartCardAuthoringAsset current;
    string currentPath;

    [MenuItem("Tools/技能编辑器/打开技能编辑器")]
    public static void Open()
    {
        instance = GetWindow<SkillEditorWindow>("技能编辑器");
        instance.minSize = new Vector2(1200, 700);
    }

    [MenuItem("Tools/技能编辑器/导出卡池JSON")]
    public static void ExportMenu() => ExportAll(interactive: true);

    [MenuItem("Tools/技能编辑器/从 parts.json 导入")]
    public static void ImportMenu()
    {
        string partsPath = FindGameData("parts.json");
        PartCardConfig[] configs = SkillJson.Deserialize<PartCardConfig[]>(File.ReadAllText(partsPath));
        HashSet<int> existing = LoadAllAssets().Keys.ToHashSet();
        int created = 0;
        foreach (PartCardConfig config in configs)
        {
            if (existing.Contains(config.Id))
            {
                Debug.Log($"[技能编辑器] 导入跳过 id {config.Id}（作者稿已存在——作者稿是唯一事实源）");
                continue;
            }
            string path = $"{PartsRoot}/{SanitizeFileName(config.Name)}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            PartCardAuthoringAsset asset = Decompile(config);
            AssetDatabase.CreateAsset(asset, path);
            created++;
        }
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("导入完成", $"新建 {created} 张卡（跳过已有 id）", "好");
        instance?.poolView?.MarkDirtyRepaint();
    }

    void OnEnable()
    {
        instance = this;
        treeView = new PartCardTreeView(new TreeViewState());
        treeView.OnCardSelected += path =>
        {
            if (!string.IsNullOrEmpty(path)) OpenCard(path); // 单击卡=打开；文件夹=保持当前
        };
        treeView.OnCardDeleted += path =>
        {
            if (path != currentPath) return;
            current = null;
            currentPath = null;
            graph.Load(null);
            propertyView?.MarkDirtyRepaint();
        };

        toolbarView = new IMGUIContainer(DrawToolbar) { style = { height = 26 } };
        graph = new SkillGraphView();
        graph.OnSelectionChanged = element =>
        {
            selection = element;
            propertyView.MarkDirtyRepaint();
        };
        graph.OnGraphDirty = () => MarkCurrentDirty();
        poolView = new IMGUIContainer(DrawCardPool) { style = { width = 280, minWidth = 240 } };
        propertyView = new IMGUIContainer(DrawPropertyPanel) { style = { width = 340, minWidth = 300 } };

        VisualElement columns = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
        columns.Add(poolView);
        columns.Add(graph);
        columns.Add(propertyView);
        rootVisualElement.Add(toolbarView);
        rootVisualElement.Add(columns);

        EditorApplication.update += PollTick;
        EditorApplication.projectChanged += OnProjectChanged;
    }

    void OnDisable()
    {
        EditorApplication.update -= PollTick;
        EditorApplication.projectChanged -= OnProjectChanged;
        SaveCurrentCard(); // 关窗自动保存（需求文档§7）
    }

    void PollTick() => graph?.PollSelection(); // 选择同步轮询（Unity 6 GraphView 无 selectionChanged 事件）

    void OnProjectChanged() => treeView?.Reload(); // Project 窗口移动/改名/删除 → 树实时同步（§3.4 等价通道）

    // ==================== 工具栏 ====================

    void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("保存", EditorStyles.toolbarButton)) SaveCurrentCard();
            if (GUILayout.Button("全览", EditorStyles.toolbarButton)) graph.FrameAll();
            if (GUILayout.Button("导出卡池JSON", EditorStyles.toolbarButton)) ExportAll(interactive: true);
            GUILayout.FlexibleSpace();
            GUILayout.Label(current != null
                ? $"当前卡：[{AssetDatabase.LoadAssetAtPath<PartCardAuthoringAsset>(currentPath)?.CardId}] {Path.GetFileNameWithoutExtension(currentPath)}"
                : "未打开卡（左栏单击一张卡）", EditorStyles.miniLabel);
        }
    }

    // ==================== 左栏：卡池资产树（PartCardTreeView——资源管理器体验，交互全在树内） ====================

    void DrawCardPool()
    {
        treeView.OnGUI(GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)));
    }

    // ==================== 右栏：参数面板（§5——点节点显示属性；未选中占位） ====================

    void DrawPropertyPanel()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            if (current == null) { EditorGUILayout.HelpBox("左栏单击一张卡打开", MessageType.Info); return; }

            string cardName = Path.GetFileNameWithoutExtension(currentPath);
            EditorGUILayout.LabelField($"{cardName} · 部位卡", EditorStyles.boldLabel);

            if (selection is RootNodeView || selection == null)
                DrawRootProperties();
            else if ((selection as PartNodeView)?.Data is PartGraphNode data)
                DrawNodeProperties(data);
        }
    }

    void DrawRootProperties()
    {
        EditorGUI.BeginChangeCheck();
        current.CardId = EditorGUILayout.IntField("卡牌 Id", current.CardId);
        current.SlotType = Popup("槽型", current.SlotType, PartCardLabels.SlotValues, PartCardLabels.SlotNames);
        current.Cost = EditorGUILayout.IntField("费用", current.Cost);
        current.DefaultTarget = Popup("技能目标", current.DefaultTarget, PartCardLabels.TargetValues, PartCardLabels.TargetNames);
        current.GrantTags = EditorGUILayout.TextField("赋予关键词(逗号分隔)", current.GrantTags);
        current.Consumable = EditorGUILayout.Toggle("消耗", current.Consumable);
        if (current.SlotType == "Body")
        {
            current.Attack = EditorGUILayout.IntField("攻击", current.Attack);
            current.MaxHp = EditorGUILayout.IntField("生命", current.MaxHp);
        }
        if (EditorGUI.EndChangeCheck())
        {
            MarkCurrentDirty();
            graph.RefreshAllSummaries(); // 槽型改动端口即时重建（§5.2）
            poolView.MarkDirtyRepaint(); // Id 改动左栏即时刷新（§5.2）
        }
    }

    void DrawNodeProperties(PartGraphNode data)
    {
        if (data is OpNodeData op)
        {
            op.Kind = Popup("操作", op.Kind, PartCardLabels.OpValues, PartCardLabels.OpNames);
            op.Target = Popup("节点目标", op.Target, PartCardLabels.NodeScopeValues, PartCardLabels.NodeScopeNames);
            switch (op.Kind)
            {
                case OpNodeKind.Damage:
                case OpNodeKind.Heal:
                    op.Amount = EditorGUILayout.DoubleField("数值", op.Amount);
                    break;
                case OpNodeKind.ModifyAttribute:
                    op.Attribute = Popup("属性", op.Attribute, PartCardLabels.AttributeValues, PartCardLabels.AttributeNames);
                    op.ModOp = Popup("修饰方式", op.ModOp, PartCardLabels.ModValues, PartCardLabels.ModNames);
                    op.Magnitude = EditorGUILayout.DoubleField("数值", op.Magnitude);
                    op.Duration = Popup("时长", op.Duration, PartCardLabels.DurationValues, PartCardLabels.DurationNames);
                    if (op.Duration == "Rounds") op.Rounds = EditorGUILayout.IntField("回合", Mathf.Max(1, op.Rounds));
                    break;
                case OpNodeKind.DrawCards:
                    op.Count = EditorGUILayout.IntField("张数", Mathf.Max(1, op.Count));
                    break;
                case OpNodeKind.GrantTag:
                    op.Tag = EditorGUILayout.TextField("标签", op.Tag);
                    op.Duration = Popup("时长", op.Duration, PartCardLabels.DurationValues, PartCardLabels.DurationNames);
                    if (op.Duration == "Rounds") op.Rounds = EditorGUILayout.IntField("回合", Mathf.Max(1, op.Rounds));
                    break;
                case OpNodeKind.Condition:
                    op.CondAttribute = Popup("属性", op.CondAttribute, PartCardLabels.AttributeValues, PartCardLabels.AttributeNames);
                    op.CondOp = Popup("运算符", op.CondOp, PartCardLabels.CompareValues, PartCardLabels.CompareNames);
                    op.CondValueType = Popup("比较值", op.CondValueType, PartCardLabels.ValueTypeValues, PartCardLabels.ValueTypeNames);
                    op.CondValue = EditorGUILayout.DoubleField("值(百分比=数字)", op.CondValue);
                    op.CondSubject = Popup("求值对象", op.CondSubject, PartCardLabels.SubjectValues, PartCardLabels.SubjectNames);
                    break;
            }
            if (GUILayout.Button("删除此节点")) graph.DeleteSelectedNodes();
        }
        else if (data is TargetSwitchNodeData ts)
        {
            ts.TargetMode = Popup("目标", ts.TargetMode, PartCardLabels.TargetValues, PartCardLabels.TargetNames);
            if (GUILayout.Button("删除此节点")) graph.DeleteSelectedNodes();
        }
        else if (data is TriggerConfigNodeData tc)
        {
            tc.EventTag = Popup("事件", tc.EventTag, PartCardLabels.EventTagValues, PartCardLabels.EventTagNames);
            tc.SelfOnly = EditorGUILayout.Toggle("仅自身承受时", tc.SelfOnly);
            tc.SourceOnly = EditorGUILayout.Toggle("仅自身造成时", tc.SourceOnly);
            tc.Once = EditorGUILayout.Toggle("每场一次", tc.Once);
            tc.HasValueFilter = EditorGUILayout.Toggle("数值过滤", tc.HasValueFilter);
            if (tc.HasValueFilter)
                tc.ValueFilterMin = EditorGUILayout.DoubleField("≥", tc.ValueFilterMin);
            if (GUILayout.Button("删除此节点")) graph.DeleteSelectedNodes();
            EditorGUILayout.HelpBox("触发链内单组单目标（TriggerDef 只装一个效果组）——链上不许切目标。", MessageType.None);
        }
        if (GUI.changed) { MarkCurrentDirty(); graph.RefreshAllSummaries(); }
    }

    // ==================== 卡打开/保存/脏标 ====================

    void OpenCard(string path)
    {
        SaveCurrentCard(); // 切换前自动保存（§7）
        currentPath = path;
        current = AssetDatabase.LoadAssetAtPath<PartCardAuthoringAsset>(path);
        graph.Load(current);
        graph.SelectRoot(); // 打开后自动选中技能节点（§5.1）
        propertyView.MarkDirtyRepaint();
    }

    void MarkCurrentDirty()
    {
        if (current != null) EditorUtility.SetDirty(current);
    }

    void SaveCurrentCard()
    {
        if (current == null) return;
        EditorUtility.SetDirty(current);
        AssetDatabase.SaveAssets();
    }

    // ==================== 导出（全库编译 → parts.json；错误汇总中止） ====================

    static void ExportAll(bool interactive)
    {
        Dictionary<int, PartCardAuthoringAsset> assets = LoadAllAssets();
        List<PartCardConfig> configs = new List<PartCardConfig>();
        List<string> errors = new List<string>();
        List<int> ids = new List<int>();
        foreach (KeyValuePair<string, PartCardAuthoringAsset> pair in AssetPaths(assets))
        {
            string name = Path.GetFileNameWithoutExtension(pair.Key);
            List<string> cardErrors = new List<string>();
            PartCardConfig config = PartCardCompiler.Compile(pair.Value, name, cardErrors);
            if (config == null)
            {
                errors.Add($"「{name}」：{string.Join("；", cardErrors)}");
                continue;
            }
            if (ids.Contains(config.Id)) errors.Add($"「{name}」：id {config.Id} 与其他卡重复（导出中止项）");
            ids.Add(config.Id);
            configs.Add(config);
        }
        if (errors.Count > 0)
        {
            string message = string.Join("\n", errors.Take(8));
            if (errors.Count > 8) message += $"\n…共 {errors.Count} 张卡有错";
            if (interactive) EditorUtility.DisplayDialog("导出中止（重复 id / 编译错误）", message, "回去改");
            Debug.LogWarning($"[技能编辑器] 导出中止：\n{message}");
            return;
        }

        string json = SkillJson.Serialize(configs.OrderBy(c => c.Id).ToArray());
        string partsPath = FindGameData("parts.json");
        File.WriteAllText(partsPath, json);

        // 导出后全量复验（序列化器不跑巡检，FusionCatalog.Load 是完整校验通道）
        string sleevesPath = FindGameData("sleeves.json");
        FusionCatalog.Load(json, File.ReadAllText(sleevesPath));

        Debug.Log($"[技能编辑器] 导出 {configs.Count} 张部位卡 → {partsPath}（FusionCatalog 复验通过）");
        if (interactive)
            EditorUtility.DisplayDialog("导出完成", $"{configs.Count} 张部位卡 → GameData/parts.json\nFusionCatalog 复验通过", "好");
    }

    // ==================== 导入（反编译：PartCardConfig → 作者稿图） ====================

    static PartCardAuthoringAsset Decompile(PartCardConfig config)
    {
        PartCardAuthoringAsset asset = CreateInstance<PartCardAuthoringAsset>();
        asset.CardId = config.Id;
        asset.SlotType = config.SlotType.ToString();
        asset.Cost = config.Cost;
        asset.Consumable = config.Consumable;
        asset.GrantTags = config.GrantTags != null ? string.Join(",", config.GrantTags) : "";
        asset.Attack = config.Attack;
        asset.MaxHp = config.MaxHp;

        float y = 0;
        void Place(PartGraphNode node, string fromKey, float x)
        {
            node.Position = new Vector2(x, y);
            y += 120;
            asset.Nodes.Add(node);
            if (fromKey != null) asset.Edges.Add(new PartGraphEdgeData { From = fromKey, To = node.Guid });
        }

        // op 子链反编译：线性链；条件节点递归挂 是/否 出口
        void WalkOps(EffectOp[] ops, string fromKey)
        {
            string cursor = fromKey;
            foreach (EffectOp op in ops ?? Array.Empty<EffectOp>())
            {
                OpNodeData node;
                switch (op)
                {
                    case DamageOp damage:
                        node = new OpNodeData { Kind = OpNodeKind.Damage, Amount = damage.Amount?.Value ?? 0 };
                        break;
                    case HealOp heal:
                        node = new OpNodeData { Kind = OpNodeKind.Heal, Amount = heal.Amount?.Value ?? 0 };
                        break;
                    case DrawCardsOp draw:
                        node = new OpNodeData { Kind = OpNodeKind.DrawCards, Count = draw.Count };
                        break;
                    case GrantTagOp grant:
                        node = new OpNodeData
                        {
                            Kind = OpNodeKind.GrantTag, Tag = grant.Tag,
                            Duration = grant.Duration.ToString(), Rounds = grant.Rounds,
                        };
                        break;
                    case ModifyAttributeOp modify:
                        node = new OpNodeData
                        {
                            Kind = OpNodeKind.ModifyAttribute,
                            Attribute = modify.Attribute.ToString(), ModOp = modify.Op.ToString(),
                            Magnitude = modify.Magnitude?.Value ?? 0,
                            Duration = modify.Duration.ToString(), Rounds = modify.Rounds,
                        };
                        break;
                    case ConditionalOp conditional:
                        node = new OpNodeData { Kind = OpNodeKind.Condition };
                        if (conditional.Condition is AttributeCompareCondition compare)
                        {
                            node.CondAttribute = compare.Attribute.ToString();
                            node.CondOp = compare.Op.ToString();
                            node.CondValueType = compare.ValueType.ToString();
                            node.CondValue = compare.Value;
                        }
                        node.CondSubject = conditional.ConditionTarget.ToString();
                        Place(node, cursor, 560);
                        WalkOps(conditional.Then, node.Guid + ":yes");
                        WalkOps(conditional.Else, node.Guid + ":no");
                        cursor = node.Guid;
                        continue;
                    default:
                        continue;
                }
                Place(node, cursor, 560);
                cursor = node.Guid;
            }
        }

        void WalkGroup(EffectGroupDef group, string port)
        {
            string entry = group.PlayAfterAnimation ? RootPorts.OnSummonLate : port;
            string cursor = entry;
            if (group.Target != TargetMode.Self)
            {
                TargetSwitchNodeData sw = new TargetSwitchNodeData { TargetMode = group.Target.ToString() };
                Place(sw, cursor, 380);
                cursor = sw.Guid;
            }
            WalkOps(group.Ops, cursor);
        }

        if (config.Effects != null)
        {
            string port = config.SlotType == SlotType.OnLeave ? RootPorts.OnLeave : RootPorts.OnSummon;
            foreach (EffectGroupDef group in config.Effects) WalkGroup(group, port);
        }
        if (config.Triggers != null)
        {
            foreach (TriggerDef trigger in config.Triggers)
            {
                TriggerConfigNodeData cfgNode = new TriggerConfigNodeData
                {
                    EventTag = trigger.EventTag,
                    SelfOnly = trigger.SelfOnly,
                    SourceOnly = trigger.SourceOnly,
                    Once = trigger.Once,
                    HasValueFilter = trigger.ValueFilter != null,
                    ValueFilterMin = trigger.ValueFilter?.Value ?? 1,
                };
                Place(cfgNode, RootPorts.ContinuousTrigger, 380);
                string cursor = cfgNode.Guid;
                if (trigger.Effects != null && trigger.Effects.Target != TargetMode.Self)
                {
                    TargetSwitchNodeData sw = new TargetSwitchNodeData { TargetMode = trigger.Effects.Target.ToString() };
                    Place(sw, cursor, 380);
                    cursor = sw.Guid;
                }
                WalkOps(trigger.Effects?.Ops, cursor);
            }
        }
        if (config.Auras != null)
        {
            foreach (GameplayEffectDef aura in config.Auras)
            {
                List<PartGraphNode> nodes = new List<PartGraphNode>();
                foreach (AttributeModifierDef modifier in aura.Modifiers ?? Array.Empty<AttributeModifierDef>())
                    nodes.Add(new OpNodeData
                    {
                        Kind = OpNodeKind.ModifyAttribute,
                        Attribute = modifier.Attribute.ToString(),
                        ModOp = modifier.Op.ToString(),
                        Magnitude = modifier.Magnitude?.Value ?? 0,
                        Duration = "Permanent",
                    });
                foreach (string tag in aura.GrantedTags ?? Array.Empty<string>())
                    nodes.Add(new OpNodeData { Kind = OpNodeKind.GrantTag, Tag = tag, Duration = "Permanent" });
                string cursor = RootPorts.ContinuousAura;
                foreach (PartGraphNode node in nodes)
                {
                    Place(node, cursor, 560);
                    cursor = node.Guid;
                }
            }
        }
        return asset;
    }

    // ==================== 选项表与支撑（中文显示=PartCardLabels；数据侧仍存英文枚举名） ====================

    /// <summary>中文下拉：显示 names、存取 values（与当前值比对取索引）。</summary>
    static string Popup(string label, string value, string[] values, string[] names)
    {
        int index = Array.IndexOf(values, value);
        if (index < 0) index = 0;
        return values[EditorGUILayout.Popup(label, index, names)];
    }

    static T Popup<T>(string label, T value, string[] values, string[] names) where T : struct, Enum =>
        (T)Enum.Parse(typeof(T), Popup(label, value.ToString(), values, names));

    static Dictionary<int, PartCardAuthoringAsset> LoadAllAssets()
    {
        Dictionary<int, PartCardAuthoringAsset> map = new Dictionary<int, PartCardAuthoringAsset>();
        foreach (string guid in AssetDatabase.FindAssets("t:PartCardAuthoringAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            PartCardAuthoringAsset asset = AssetDatabase.LoadAssetAtPath<PartCardAuthoringAsset>(path);
            if (asset != null) map[asset.CardId] = asset;
        }
        return map;
    }

    static Dictionary<string, PartCardAuthoringAsset> AssetPaths(Dictionary<int, PartCardAuthoringAsset> byId)
    {
        Dictionary<string, PartCardAuthoringAsset> map = new Dictionary<string, PartCardAuthoringAsset>();
        foreach (PartCardAuthoringAsset asset in byId.Values)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(path)) map[path] = asset;
        }
        return map;
    }

    static string FindGameData(string fileName)
    {
        DirectoryInfo dir = new DirectoryInfo(Application.dataPath).Parent?.Parent; // Assets→Unity→仓库根
        string path = Path.Combine(dir?.FullName ?? "", "GameData", fileName);
        if (!File.Exists(path))
            throw new InvalidOperationException($"未找到 {path}");
        return path;
    }

    static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }
}
