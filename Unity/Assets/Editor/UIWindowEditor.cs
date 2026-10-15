using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;
using System.Collections.Generic;

[CustomEditor(typeof(UIWindow), true)]
[CanEditMultipleObjects]
public class UIWindowEditor : Editor
{
    [MenuItem("GameObject/UIWindow/改名为 Image(&)", false, 1010)]
    private static void RenameSelectedAsImage() { RenameSelectedWithMode("Image", "&"); }

    [MenuItem("GameObject/UIWindow/改名为 Button(&)", false, 1011)]
    private static void RenameSelectedAsButton() { RenameSelectedWithMode("Button", "&"); }

    [MenuItem("GameObject/UIWindow/改名为 Text(&)", false, 1012)]
    private static void RenameSelectedAsText() { RenameSelectedWithMode("Text", "&"); }

    [MenuItem("GameObject/UIWindow/改名为 GameObject(&)", false, 1013)]
    private static void RenameSelectedAsGameObject() { RenameSelectedWithMode("GameObject", "&"); }

    [MenuItem("GameObject/UIWindow/改名为 UIWindow($)", false, 1014)]
    private static void RenameSelectedAsWindow() { RenameSelectedWithMode("UIWindow", "$"); }

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
            var win = (UIWindow)target;
            Undo.RecordObject(win, "扫描子节点");
            var scanResult = ScanWindow(win.transform);

            win.SetImageRefs(scanResult.imageList);
            win.SetButtonRefs(scanResult.buttonList);
            win.SetTextRefs(scanResult.textList);
            win.SetWindowRefs(scanResult.windowList);
            win.SetGenericRefs(scanResult.genericList);
            //win.BuildDictionaries();

            EditorUtility.SetDirty(win);
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
            // $ 只允许 UIWindow 类型
            return "$_node_" + key + "_$";
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

    private static void RenameScannedObject(Transform t, string typeStr, string newKey)
    {
        string newName;
        if (typeStr == "$")
        {
            newName = "$_node_" + newKey + "_$";
        }
        else
        {
            newName = "&_" + typeStr + "_" + newKey + "_&";
        }

        Undo.RecordObject(t.gameObject, "扫描重名改名");
        t.name = newName;
    }

    private static ScanData ScanWindow(Transform root)
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

            // $ 仅允许 UIWindow：$_node_name_$，并且不继续扫描当前节点的子物体
            var mNode = Regex.Match(name, "\\$_node_(\\w+)_\\$");
            if (mNode.Success)
            {
                var key = mNode.Groups[1].Value;
                if (usedKeys.Contains(key))
                {
                    key = MakeUniqueKey(key, usedKeys);
                    RenameScannedObject(child, "$", key);
                    Debug.LogWarning("[UIWindow] 重名检测：key='" + mNode.Groups[1].Value + "' 已存在，已改名为 '" + child.name + "'");
                }
                usedKeys.Add(key);
                result.windowList.Add(new UIWindow.RefEntry<UIWindow>
                {
                    key = key,
                    go = child.gameObject,
                    comp = child.GetComponent<UIWindow>(),
                });

                continue; // $ 规则不继续扫描子物体
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
                    Debug.LogWarning("[UIWindow] 重名检测：key='" + mType.Groups[2].Value + "' 已存在，已改名为 '" + child.name + "'");
                }
                usedKeys.Add(key);
                switch (typeStr)
                {
                    case "image":
                        result.imageList.Add(new UIWindow.RefEntry<UnityEngine.UI.Image> { key = key, go = child.gameObject, comp = child.GetComponent<UnityEngine.UI.Image>() });
                        break;
                    case "button":
                        result.buttonList.Add(new UIWindow.RefEntry<UnityEngine.UI.Button> { key = key, go = child.gameObject, comp = child.GetComponent<UnityEngine.UI.Button>() });
                        break;
                    case "text":
                        result.textList.Add(new UIWindow.RefEntry<TMPro.TMP_Text> { key = key, go = child.gameObject, comp = child.GetComponent<TMPro.TMP_Text>() });
                        break;
                    case "gameobject":
                        result.genericList.Add(new UIWindow.RefEntry { key = key, go = child.gameObject });
                        break;
                }
            }

            // & 规则继续递归扫描子对象
            ScanTransform(child, result, usedKeys);
        }
    }

    private class ScanData
    {
        public readonly List<UIWindow.RefEntry<UnityEngine.UI.Image>> imageList = new List<UIWindow.RefEntry<UnityEngine.UI.Image>>();
        public readonly List<UIWindow.RefEntry<UnityEngine.UI.Button>> buttonList = new List<UIWindow.RefEntry<UnityEngine.UI.Button>>();
        public readonly List<UIWindow.RefEntry<TMPro.TMP_Text>> textList = new List<UIWindow.RefEntry<TMPro.TMP_Text>>();
        public readonly List<UIWindow.RefEntry<UIWindow>> windowList = new List<UIWindow.RefEntry<UIWindow>>();
        public readonly List<UIWindow.RefEntry> genericList = new List<UIWindow.RefEntry>();
    }
}
