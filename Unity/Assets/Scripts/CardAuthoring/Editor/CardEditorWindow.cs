using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardAuthoring.Editor
{
    /// <summary>
    /// 技能编辑器主窗口：三栏布局（左资产列表 / 中 GraphView 画布 / 右卡级+节点 Inspector），
    /// 仿参考项目 XHFrameworkSkillDemo 的技能编辑器交互。
    /// 保存时机：按钮/Ctrl+S、切换资产或档位前、关窗时（自动）。
    /// </summary>
    public class CardEditorWindow : EditorWindow
    {
        internal const string AuthoringRoot = "Assets/CardAuthoring";

        EffectCardAsset effectCard;
        PartCardAsset partCard;
        int tierIndex;
        bool dirty;

        EffectGraphView graph;
        ListView effectList;
        ListView partList;
        VisualElement cardForm;
        NodeInspectorView nodeInspector;

        [MenuItem("Tools/技能编辑器")]
        public static void Open() => GetWindow<CardEditorWindow>("技能编辑器");

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            Toolbar toolbar = new Toolbar();
            toolbar.Add(new Button(Save) { text = "保存 (Ctrl+S)" });
            toolbar.Add(new Button(() => graph?.FrameAll()) { text = "全览" });
            toolbar.Add(new Button(CreateEffectCard) { text = "新建效果卡" });
            toolbar.Add(new Button(CreatePartCard) { text = "新建部位卡" });
            toolbar.Add(new Button(DeleteCurrent) { text = "删除当前卡" });
            toolbar.Add(new Button(() => CardPoolExporter.ExportFromMenu()) { text = "导出卡池 JSON" });
            root.Add(toolbar);

            VisualElement main = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };

            // 左栏：卡池资产列表
            VisualElement left = new VisualElement { style = { width = 230, borderRightWidth = 1, paddingLeft = 4, paddingTop = 4 } };
            left.Add(BoldLabel("效果卡"));
            effectList = MakeAssetList(a => a is EffectCardAsset e ? $"{e.id}  {e.cardName}" : "");
            left.Add(effectList);
            left.Add(BoldLabel("部位卡"));
            partList = MakeAssetList(a => a is PartCardAsset p ? $"{p.id}  {p.cardName}" : "");
            left.Add(partList);
            main.Add(left);

            // 中栏：画布
            graph = new EffectGraphView(MarkDirty);
            graph.selectionChanged += selection =>
                nodeInspector.Show(selection?.OfType<GraphElement>().FirstOrDefault(), MarkDirty);
            graph.style.flexGrow = 1;
            main.Add(graph);

            // 右栏：卡级表单 + 节点参数
            VisualElement right = new ScrollView { style = { width = 330, borderLeftWidth = 1 } };
            cardForm = new VisualElement();
            nodeInspector = new NodeInspectorView();
            right.Add(cardForm);
            right.Add(nodeInspector);
            main.Add(right);

            root.Add(main);
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.ctrlKey && evt.key == "s")
                {
                    evt.StopPropagation();
                    Save();
                }
            }, TrickleDown.TrickleDown);

            RefreshLists();
            LoadCurrent();
        }

        static Label BoldLabel(string text) => new Label(text)
        {
            style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6, marginBottom = 2 },
        };

        ListView MakeAssetList(System.Func<ScriptableObject, string> labelOf)
        {
            ListView list = new ListView { selectionType = SelectionType.Single };
            list.makeItem = () => new Label();
            list.bindItem = (element, i) =>
            {
                var items = (IList<ScriptableObject>)list.itemsSource;
                ((Label)element).text = labelOf(items[i]);
            };
            list.selectionChanged += items =>
            {
                if (items.Count > 0) SelectAsset((ScriptableObject)items[0]);
            };
            return list;
        }

        // ---------- 当前图数据 ----------

        List<EffectDefNodeData> CurDefs =>
            effectCard != null ? effectCard.tiers[tierIndex].effectDefs : partCard?.effectDefs;
        List<OpNodeData> CurOps =>
            effectCard != null ? effectCard.tiers[tierIndex].opNodes : partCard?.opNodes;
        List<ConnectionData> CurConnections =>
            effectCard != null ? effectCard.tiers[tierIndex].connections : partCard?.connections;

        // ---------- 加载 / 保存 ----------

        void SelectAsset(ScriptableObject asset)
        {
            SaveIfDirty();
            effectCard = asset as EffectCardAsset;
            partCard = asset as PartCardAsset;
            tierIndex = 0;
            LoadCurrent();
        }

        void LoadCurrent()
        {
            graph?.LoadGraph(CurDefs, CurOps, CurConnections);
            RebuildCardForm();
            nodeInspector?.Show(null, MarkDirty);
        }

        void RebuildCardForm()
        {
            cardForm.Clear();
            if (effectCard != null)
            {
                cardForm.Add(CardInspectorView.Build(effectCard, MarkDirty, SwitchTier, tierIndex));
            }
            else if (partCard != null)
            {
                cardForm.Add(CardInspectorView.Build(partCard, MarkDirty));
            }
        }

        void SwitchTier(int index)
        {
            SaveIfDirty();
            tierIndex = Mathf.Clamp(index, 0, effectCard.tiers.Count - 1);
            LoadCurrent();
        }

        public void Save()
        {
            if (graph == null) return;
            if (effectCard == null && partCard == null) return;

            graph.SyncBackData(CurDefs, CurOps, CurConnections);
            Object asset = (Object)effectCard ?? partCard;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            dirty = false;
            titleContent = new GUIContent("技能编辑器");
        }

        void SaveIfDirty()
        {
            if (dirty) Save();
        }

        void MarkDirty()
        {
            dirty = true;
            titleContent = new GUIContent("技能编辑器 *");
        }

        void OnDisable() => SaveIfDirty();

        // ---------- 资产管理 ----------

        void RefreshLists()
        {
            effectList.itemsSource = LoadAll<EffectCardAsset>("t:EffectCardAsset")
                .Cast<ScriptableObject>().ToList();
            partList.itemsSource = LoadAll<PartCardAsset>("t:PartCardAsset")
                .Cast<ScriptableObject>().ToList();
            effectList.Rebuild();
            partList.Rebuild();
        }

        static List<T> LoadAll<T>(string filter) where T : ScriptableObject
        {
            EnsureFolders();
            return AssetDatabase.FindAssets(filter, new[] { AuthoringRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .Select(path => AssetDatabase.LoadAssetAtPath<T>(path))
                .Where(asset => asset != null)
                .ToList();
        }

        internal static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(AuthoringRoot))
                AssetDatabase.CreateFolder("Assets", "CardAuthoring");
            if (!AssetDatabase.IsValidFolder($"{AuthoringRoot}/EffectCards"))
                AssetDatabase.CreateFolder(AuthoringRoot, "EffectCards");
            if (!AssetDatabase.IsValidFolder($"{AuthoringRoot}/PartCards"))
                AssetDatabase.CreateFolder(AuthoringRoot, "PartCards");
        }

        void CreateEffectCard()
        {
            SaveIfDirty();
            EnsureFolders();
            EffectCardAsset asset = CreateInstance<EffectCardAsset>();
            asset.id = NextId(LoadAll<EffectCardAsset>("t:EffectCardAsset"));
            asset.cardName = $"新效果卡{asset.id}";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{AuthoringRoot}/EffectCards/效果卡_{asset.id}.asset");
            AssetDatabase.CreateAsset(asset, path);
            RefreshLists();
            SelectAsset(asset);
        }

        void CreatePartCard()
        {
            SaveIfDirty();
            EnsureFolders();
            PartCardAsset asset = CreateInstance<PartCardAsset>();
            asset.id = NextId(LoadAll<PartCardAsset>("t:PartCardAsset"));
            asset.cardName = $"新部位卡{asset.id}";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{AuthoringRoot}/PartCards/部位卡_{asset.id}.asset");
            AssetDatabase.CreateAsset(asset, path);
            RefreshLists();
            SelectAsset(asset);
        }

        static int NextId<T>(List<T> assets) where T : ScriptableObject
        {
            int max = 0;
            foreach (T a in assets)
            {
                int id = a is EffectCardAsset e ? e.id : a is PartCardAsset p ? p.id : 0;
                if (id > max) max = id;
            }
            return max + 1;
        }

        void DeleteCurrent()
        {
            Object asset = (Object)effectCard ?? partCard;
            if (asset == null) return;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return;

            effectCard = null;
            partCard = null;
            AssetDatabase.DeleteAsset(path);
            graph?.LoadGraph(null, null, null);
            cardForm.Clear();
            nodeInspector.Show(null, MarkDirty);
            dirty = false;
            titleContent = new GUIContent("技能编辑器");
            RefreshLists();
        }
    }
}
