using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// 部位卡资产树（M5 验收反馈重做，参照 XHFrameworkSkillDemo 的 SkillAssetTreeView 模式——资源管理器体验）：
/// 文件夹/卡双层树（图标+边框+滚动）；单击卡=打开、单击文件夹=选中；双击文件夹=展开/收起、双击卡=重命名（§3.3）；
/// 右键菜单（新建卡/新建文件夹/重命名/删除；空白处右键作用于选中文件夹，无选中=分区根 §3.2）；
/// 拖拽移动（卡与文件夹均可拖入文件夹，禁拖入自身子孙，§3.4）。
/// 唯一事实源 = Assets/Editor/SkillCards/Parts 下的 PartCardAuthoringAsset（每卡一 .asset，文件名=卡名，条目显示「id 卡名」§3.1）。
/// 路径注意：AssetDatabase 一律正斜杠，而 Windows 的 Path.GetDirectoryName 会规范成反斜杠——比较前必须统一（本文件已统一封装 NormalizeDir）。
/// </summary>
public sealed class PartCardTreeView : TreeView
{
    const string RootPath = "Assets/Editor/SkillCards/Parts";

    /// <summary>单击卡（path）/单击文件夹（null）——窗口据此开卡。</summary>
    public event Action<string> OnCardSelected;
    /// <summary>删除了卡（path）——窗口清画布用。</summary>
    public event Action<string> OnCardDeleted;

    readonly Dictionary<int, string> idToPath = new Dictionary<int, string>();
    int nextId = 1;

    public PartCardTreeView(TreeViewState state) : base(state)
    {
        showAlternatingRowBackgrounds = false;
        showBorder = true;
        useScrollView = true;
        Reload();
    }

    // ==================== 建树 ====================

    protected override TreeViewItem BuildRoot()
    {
        idToPath.Clear();
        nextId = 1;
        EnsureRootFolder();

        TreeViewItem root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
        TreeViewItem rootFolder = new TreeViewItem
        {
            id = nextId++, depth = 0, displayName = "部位卡", icon = FolderIcon,
        };
        idToPath[rootFolder.id] = RootPath;
        root.AddChild(rootFolder);

        BuildChildren(RootPath, rootFolder, 1);
        SetupDepthsFromParentsAndChildren(root);
        SetExpanded(rootFolder.id, true);
        return root;
    }

    void BuildChildren(string folder, TreeViewItem parent, int depth)
    {
        foreach (string dir in AssetDatabase.GetSubFolders(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            TreeViewItem item = new TreeViewItem
            {
                id = nextId++, depth = depth, displayName = Path.GetFileName(dir), icon = FolderIcon,
            };
            idToPath[item.id] = dir;
            parent.AddChild(item);
            BuildChildren(dir, item, depth + 1);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:PartCardAuthoringAsset", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (NormalizeDir(path) != folder) continue; // 只列本级（反斜杠陷阱：比较前统一分隔符）
            PartCardAuthoringAsset asset = AssetDatabase.LoadAssetAtPath<PartCardAuthoringAsset>(path);
            if (asset == null) continue;
            TreeViewItem item = new TreeViewItem
            {
                id = nextId++, depth = depth,
                displayName = $"{asset.CardId}  {Path.GetFileNameWithoutExtension(path)}",
                icon = CardIcon,
            };
            idToPath[item.id] = path;
            parent.AddChild(item);
        }
    }

    static void EnsureRootFolder()
    {
        if (AssetDatabase.IsValidFolder(RootPath)) return;
        if (!AssetDatabase.IsValidFolder("Assets/Editor")) AssetDatabase.CreateFolder("Assets", "Editor");
        if (!AssetDatabase.IsValidFolder("Assets/Editor/SkillCards")) AssetDatabase.CreateFolder("Assets/Editor", "SkillCards");
        AssetDatabase.CreateFolder("Assets/Editor/SkillCards", "Parts");
    }

    static Texture2D FolderIcon => EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
    static Texture2D CardIcon => EditorGUIUtility.IconContent("ScriptableObject Icon").image as Texture2D;
    static string NormalizeDir(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";

    string PathOf(int id) => idToPath.TryGetValue(id, out string path) ? path : null;

    // ==================== 交互（资源管理器体验） ====================

    protected override void SingleClickedItem(int id)
    {
        base.SingleClickedItem(id);
        string path = PathOf(id);
        if (path == null) return;
        if (Directory.Exists(path)) OnCardSelected?.Invoke(null); // 文件夹=仅选中（窗口保持当前卡）
        else OnCardSelected?.Invoke(path);                        // 卡=打开（切换前窗口自动保存）
    }

    protected override void DoubleClickedItem(int id)
    {
        string path = PathOf(id);
        if (path == null) return;
        if (Directory.Exists(path)) { SetExpanded(id, !IsExpanded(id)); return; } // 双击文件夹=展开/收起（§3.3）
        OpenRename(path);                                                          // 双击卡=重命名（§3.3）
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        base.RowGUI(args);
        if (Event.current.type == EventType.ContextClick && args.rowRect.Contains(Event.current.mousePosition))
        {
            ContextClickedItem(args.item.id);
            Event.current.Use();
        }
    }

    protected override void ContextClickedItem(int id)
    {
        string path = PathOf(id);
        if (string.IsNullOrEmpty(path)) return;

        GenericMenu menu = new GenericMenu();
        if (Directory.Exists(path))
        {
            menu.AddItem(new GUIContent("新建卡"), false, () => CreateCard(path));
            menu.AddItem(new GUIContent("新建文件夹"), false, () => CreateFolder(path));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("重命名"), false, () => OpenRename(path));
            if (path == RootPath) menu.AddDisabledItem(new GUIContent("删除文件夹（连同全部内容）"));
            else menu.AddItem(new GUIContent("删除文件夹（连同全部内容）"), false, () => DeleteAsset(path, isFolder: true));
        }
        else
        {
            menu.AddItem(new GUIContent("重命名"), false, () => OpenRename(path));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除卡"), false, () => DeleteAsset(path, isFolder: false));
        }
        menu.ShowAsContext();
    }

    protected override void ContextClicked() // 空白处：作用于选中文件夹（无选中=分区根，§3.2）
    {
        string folder = RootPath;
        if (GetSelection().Count > 0 && PathOf(GetSelection()[0]) is string selected && selected != null && Directory.Exists(selected))
            folder = selected;
        string where = folder == RootPath ? "部位卡" : Path.GetFileName(folder);
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent($"在「{where}」中/新建卡"), false, () => CreateCard(folder));
        menu.AddItem(new GUIContent($"在「{where}」中/新建文件夹"), false, () => CreateFolder(folder));
        menu.ShowAsContext();
    }

    // ==================== 资产操作 ====================

    void CreateCard(string folder)
    {
        int nextCardId = MaxCardId() + 1; // 同类型现有最大 +1（§3.2）
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/新部位卡.asset");
        PartCardAuthoringAsset asset = ScriptableObject.CreateInstance<PartCardAuthoringAsset>();
        asset.CardId = nextCardId;
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Reload();
        SelectNew(path, openCard: true);
    }

    static int MaxCardId()
    {
        int max = 100;
        foreach (string guid in AssetDatabase.FindAssets("t:PartCardAuthoringAsset"))
        {
            PartCardAuthoringAsset asset =
                AssetDatabase.LoadAssetAtPath<PartCardAuthoringAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null && asset.CardId > max) max = asset.CardId;
        }
        return max;
    }

    void CreateFolder(string folder)
    {
        string unique = AssetDatabase.GenerateUniqueAssetPath($"{folder}/新文件夹");
        string guid = AssetDatabase.CreateFolder(folder, Path.GetFileName(unique));
        if (string.IsNullOrEmpty(guid)) return;
        AssetDatabase.Refresh();
        Reload();
        SelectNew(AssetDatabase.GUIDToAssetPath(guid), openCard: false);
    }

    /// <summary>新建后：展开父级、选中新项、（卡）打开编辑并直接进入重命名（XH 同款体验）。</summary>
    void SelectNew(string path, bool openCard)
    {
        foreach (KeyValuePair<int, string> pair in idToPath)
        {
            if (pair.Value != path) continue;
            SetExpanded(ParentId(pair.Key), true);
            SetSelection(new List<int> { pair.Key });
            if (openCard) OnCardSelected?.Invoke(path);
            OpenRename(path);
            return;
        }
    }

    int ParentId(int childId) => FindItem(childId, rootItem)?.parent?.id ?? 0;

    void OpenRename(string path)
    {
        bool isFolder = Directory.Exists(path);
        string oldName = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        RenamePopup popup = ScriptableObject.CreateInstance<RenamePopup>();
        popup.Initialize(oldName, newName =>
        {
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
            newName = Sanitize(newName);
            string newPath = NormalizeDir(path) + "/" + newName + (isFolder ? "" : ".asset");
            string error = AssetDatabase.MoveAsset(path, newPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogError($"[技能编辑器] 重命名失败：{error}");
                return;
            }
            AssetDatabase.Refresh();
            Reload();
        });
        popup.ShowUtility();
    }

    static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    void DeleteAsset(string path, bool isFolder)
    {
        string name = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        string tip = isFolder ? $"将连同全部内容删除：\n{name}" : $"确定删除「{name}」？";
        if (!EditorUtility.DisplayDialog(isFolder ? "删除文件夹" : "删除卡", tip, "删除", "取消")) return;
        if (!isFolder) OnCardDeleted?.Invoke(path);
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.Refresh();
        Reload();
    }

    // ==================== 拖拽移动（§3.4：卡/文件夹拖入文件夹；禁拖入自身子孙） ====================

    protected override bool CanStartDrag(CanStartDragArgs args)
    {
        string path = PathOf(args.draggedItem.id);
        return !string.IsNullOrEmpty(path) && path != RootPath;
    }

    protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
    {
        DragAndDrop.PrepareStartDrag();
        List<string> dragged = new List<string>();
        foreach (int id in args.draggedItemIDs)
        {
            string path = PathOf(id);
            if (!string.IsNullOrEmpty(path)) dragged.Add(path);
        }
        DragAndDrop.paths = dragged.ToArray();
        DragAndDrop.SetGenericData("PartCardTreeDrag", dragged);
        DragAndDrop.StartDrag("移动卡/文件夹");
    }

    protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
    {
        if (!(DragAndDrop.GetGenericData("PartCardTreeDrag") is List<string> dragged) || dragged.Count == 0)
            return DragAndDropVisualMode.None;

        string target = RootPath;
        if (args.parentItem != null && args.parentItem.id != 0)
        {
            string parentPath = PathOf(args.parentItem.id);
            if (!string.IsNullOrEmpty(parentPath) && Directory.Exists(parentPath)) target = parentPath;
        }

        foreach (string source in dragged) // 禁拖入自身或自身子孙
            if (target == source || target.StartsWith(source + "/", StringComparison.Ordinal))
                return DragAndDropVisualMode.Rejected;

        if (args.performDrop)
        {
            foreach (string source in dragged)
            {
                string dest = target + "/" + Path.GetFileName(source);
                if (source == dest) continue;
                if (File.Exists(dest) || Directory.Exists(dest))
                {
                    Debug.LogWarning($"[技能编辑器] 目标已存在，跳过：{dest}");
                    continue;
                }
                string error = AssetDatabase.MoveAsset(source, dest);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"[技能编辑器] 移动失败：{error}");
            }
            AssetDatabase.Refresh();
            Reload();
        }
        return DragAndDropVisualMode.Move;
    }
}

/// <summary>重命名弹窗（XH 同款：主窗居中、自动聚焦、回车确认/Esc 取消）。</summary>
public sealed class RenamePopup : EditorWindow
{
    string newName;
    Action<string> onConfirm;
    bool focused;

    public void Initialize(string currentName, Action<string> callback)
    {
        newName = currentName;
        onConfirm = callback;
        titleContent = new GUIContent("重命名");
        Vector2 size = new Vector2(320, 96);
        minSize = size;
        maxSize = size;
        focused = false;
        Rect main = EditorGUIUtility.GetMainWindowPosition();
        position = new Rect(main.x + (main.width - size.x) / 2, main.y + (main.height - size.y) / 2, size.x, size.y);
    }

    void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("新名称：", EditorStyles.boldLabel);
        GUI.SetNextControlName("RenameField");
        newName = EditorGUILayout.TextField(newName);
        if (!focused)
        {
            EditorGUI.FocusTextInControl("RenameField");
            focused = true;
        }
        if (Event.current.type == EventType.KeyDown)
        {
            if (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
            {
                Confirm();
                Event.current.Use();
            }
            else if (Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
            }
        }
        GUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("确定", GUILayout.Width(80))) Confirm();
            if (GUILayout.Button("取消", GUILayout.Width(80))) Close();
            GUILayout.FlexibleSpace();
        }
    }

    void Confirm()
    {
        onConfirm?.Invoke(newName);
        Close();
    }
}
