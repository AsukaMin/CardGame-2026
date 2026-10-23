using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Collections.Generic;

[CustomEditor(typeof(UITable))]
[CanEditMultipleObjects]
public class UITableEditor : Editor
{
    [MenuItem("GameObject/UITable/改名为 Image(&)", false, 1010)]
    private static void RenameSelectedAsImage() { RenameSelectedWithMode("Image", "&"); }

    [MenuItem("GameObject/UITable/改名为 Button(&)", false, 1011)]
    private static void RenameSelectedAsButton() { RenameSelectedWithMode("Button", "&"); }

    [MenuItem("GameObject/UITable/改名为 Text(&)", false, 1012)]
    private static void RenameSelectedAsText() { RenameSelectedWithMode("Text", "&"); }

    [MenuItem("GameObject/UITable/改名为 GameObject(&)", false, 1013)]
    private static void RenameSelectedAsGameObject() { RenameSelectedWithMode("GameObject", "&"); }

    [MenuItem("GameObject/UITable/改名为 UIWindow($)", false, 1014)]
    private static void RenameSelectedAsWindow() { RenameSelectedWithMode("UIWindow", "$"); }

    [MenuItem("GameObject/UITable/改名为 UITable($)", false, 1015)]
    private static void RenameSelectedAsTable() { RenameSelectedWithMode("UITable", "$"); }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        if (EditorApplication.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("扫描子节点仅在编辑态可用。", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("扫描子节点", GUILayout.Height(28)))
        {
            var table = (UITable)target;
            Undo.RecordObject(table, "扫描子节点");
            var scanResult = ScanTable(table.transform);

            table.SetImageRefs(scanResult.imageList);
            table.SetButtonRefs(scanResult.buttonList);
            table.SetTextRefs(scanResult.textList);
            table.SetWindowRefs(scanResult.windowList);
            table.SetTableRefs(scanResult.tableList);
            table.SetGenericRefs(scanResult.genericList);

            EditorUtility.SetDirty(table);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void RenameSelectedWithMode(string mode, string prefix)
    {
        foreach (var go in Selection.gameObjects)
        {
            if (go == null) continue;
            RenameGameObject(go, mode, prefix);
        }
    }

    private static void RenameGameObject(GameObject go, string mode, string prefix)
    {
        if (go == null) return;
        var parent = go.transform.parent;

        var siblingNames = new HashSet<string>();
        if (parent != null)
        {
            foreach (Transform child in parent)
            {
                if (child != go.transform)
                {
                    siblingNames.Add(child.name);
                }
            }
        }

        var rawName = go.name;
        var key = NormalizeKey(rawName);
        var newName = BuildPattern(mode, key, prefix);

        var finalName = newName;
        var index = 1;
        while (siblingNames.Contains(finalName))
        {
            finalName = BuildPattern(mode, key + "_" + index, prefix);
            index++;
        }

        Undo.RecordObject(go, "重命名对象");
        go.name = finalName;
    }

    private static string BuildPattern(string mode, string key, string prefix)
    {
        if (prefix == "$")
        {
            // $ 是扫描边界：UIWindow 嵌套子窗口 / UITable 嵌套引用表
            switch (mode)
            {
                case "UIWindow": return "$_node_" + key + "_$";
                case "UITable": return "$_table_" + key + "_$";
                default: return "$_node_" + key + "_$";
            }
        }

        // & 只允许 Image / Button / Text / GameObject
        switch (mode)
        {
            case "Image": return "&_image_" + key + "_&";
            case "Button": return "&_button_" + key + "_&";
            case "Text": return "&_text_" + key + "_&";
            case "GameObject": return "&_gameobject_" + key + "_&";
            default: return "&_" + mode.ToLower() + "_" + key + "_&";
        }
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrEmpty(value)) return "unnamed";
        var result = value.Trim();
        result = result.Replace(' ', '_');
        result = Regex.Replace(result, "[^A-Za-z0-9_]", "_");
        result = Regex.Replace(result, "_+", "_");
        result = result.Trim('_');
        return string.IsNullOrEmpty(result) ? "unnamed" : result;
    }

    private static string MakeUniqueKey(string key, HashSet<string> usedKeys)
    {
        var index = 1;
        var newKey = key + "_" + index;
        while (usedKeys.Contains(newKey))
        {
            index++;
            newKey = key + "_" + index;
        }
        return newKey;
    }

    // kind："$"=嵌套子窗口，"$table"=嵌套引用表，其余为 & 类型名
    private static void RenameScannedObject(Transform t, string kind, string newKey)
    {
        string newName;
        if (kind == "$")
        {
            newName = "$_node_" + newKey + "_$";
        }
        else if (kind == "$table")
        {
            newName = "$_table_" + newKey + "_$";
        }
        else
        {
            newName = "&_" + kind + "_" + newKey + "_&";
        }

        Undo.RecordObject(t.gameObject, "扫描重名改名");
        t.name = newName;
    }

    private static ScanData ScanTable(Transform root)
    {
        var result = new ScanData();
        var usedKeys = new HashSet<string>();
        ScanTransform(root, result, usedKeys);
        return result;
    }

    private static void ScanTransform(Transform parent, ScanData result, HashSet<string> usedKeys)
    {
        foreach (Transform child in parent)
        {
            var name = child.name;

            // $ 是扫描边界：$_node_name_$ 嵌套子窗口 / $_table_name_$ 嵌套引用表，并且不继续扫描当前节点的子物体
            var mNode = Regex.Match(name, "\\$_(node|table)_(\\w+)_\\$");
            if (mNode.Success)
            {
                var isWindow = mNode.Groups[1].Value == "node";
                var key = mNode.Groups[2].Value;
                if (usedKeys.Contains(key))
                {
                    key = MakeUniqueKey(key, usedKeys);
                    RenameScannedObject(child, isWindow ? "$" : "$table", key);
                    Debug.LogWarning("[UITable] 重名检测：key='" + mNode.Groups[2].Value + "' 已存在，已改名为 '" + child.name + "'");
                }
                usedKeys.Add(key);
                if (isWindow)
                {
                    result.windowList.Add(new UITable.RefEntry<UIWindow>
                    {
                        key = key,
                        go = child.gameObject,
                        comp = child.GetComponent<UIWindow>(),
                    });
                }
                else
                {
                    var comp = child.GetComponent<UITable>();
                    if (comp == null)
                        Debug.LogWarning("[UITable] 节点 '" + child.name + "' 按约定是嵌套引用表，但未挂 UITable 组件；运行期只能 GetGameObject 取到该节点");
                    result.tableList.Add(new UITable.RefEntry<UITable>
                    {
                        key = key,
                        go = child.gameObject,
                        comp = comp,
                    });
                }

                continue; // $ 规则不继续扫描子物体，嵌套表/子窗口的控件由它自己的扫描负责
            }

            // & 仅允许 Image / Button / Text / GameObject：&_image_name_& etc.
            var mType = Regex.Match(name, "&_\\s*(image|button|text|gameobject)_(\\w+)_&");
            if (mType.Success)
            {
                var typeStr = mType.Groups[1].Value.ToLower();
                var key = mType.Groups[2].Value;
                if (usedKeys.Contains(key))
                {
                    key = MakeUniqueKey(key, usedKeys);
                    RenameScannedObject(child, typeStr, key);
                    Debug.LogWarning("[UITable] 重名检测：key='" + mType.Groups[2].Value + "' 已存在，已改名为 '" + child.name + "'");
                }
                usedKeys.Add(key);
                switch (typeStr)
                {
                    case "image":
                        result.imageList.Add(new UITable.RefEntry<UnityEngine.UI.Image> { key = key, go = child.gameObject, comp = child.GetComponent<UnityEngine.UI.Image>() });
                        break;
                    case "button":
                        result.buttonList.Add(new UITable.RefEntry<UnityEngine.UI.Button> { key = key, go = child.gameObject, comp = child.GetComponent<UnityEngine.UI.Button>() });
                        break;
                    case "text":
                        result.textList.Add(new UITable.RefEntry<TMPro.TMP_Text> { key = key, go = child.gameObject, comp = child.GetComponent<TMPro.TMP_Text>() });
                        break;
                    case "gameobject":
                        result.genericList.Add(new UITable.RefEntry { key = key, go = child.gameObject });
                        break;
                }
            }

            // & 规则继续递归扫描子对象
            ScanTransform(child, result, usedKeys);
        }
    }

    private class ScanData
    {
        public readonly List<UITable.RefEntry<UnityEngine.UI.Image>> imageList = new List<UITable.RefEntry<UnityEngine.UI.Image>>();
        public readonly List<UITable.RefEntry<UnityEngine.UI.Button>> buttonList = new List<UITable.RefEntry<UnityEngine.UI.Button>>();
        public readonly List<UITable.RefEntry<TMPro.TMP_Text>> textList = new List<UITable.RefEntry<TMPro.TMP_Text>>();
        public readonly List<UITable.RefEntry<UIWindow>> windowList = new List<UITable.RefEntry<UIWindow>>();
        public readonly List<UITable.RefEntry<UITable>> tableList = new List<UITable.RefEntry<UITable>>();
        public readonly List<UITable.RefEntry> genericList = new List<UITable.RefEntry>();
    }
}

// GameObject 条目（&_gameobject_key_&）没有 comp 字段可点，把隐藏的 go 画成对象槽，点一下即可在 Hierarchy 里 ping 定位
[CustomPropertyDrawer(typeof(UITable.RefEntry))]
public class UITableRefEntryDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var key = property.FindPropertyRelative("key");
        var go = property.FindPropertyRelative("go");

        EditorGUI.BeginProperty(position, label, property);
        var keyRect = position;
        keyRect.width = position.width * 0.5f;
        var goRect = position;
        goRect.x = keyRect.xMax + 2f;
        goRect.width = position.width - keyRect.width - 2f;

        EditorGUI.TextField(keyRect, key.stringValue);
        EditorGUI.PropertyField(goRect, go, GUIContent.none);
        EditorGUI.EndProperty();
    }
}
