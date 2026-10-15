using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>窗口层级，决定 UIManager 把窗口实例化到哪一层（子类用 Layer 属性返回）。</summary>
public enum UIWindowLayer
{
    Fullscreen, // 全屏窗：互斥，开新顶旧
    Popup,      // 弹窗：可叠加，按栈回退
    Tip,        // 提示：过路即走，不进栈
}

/// <summary>
/// 窗口基类。每个窗口 = Resources/UI/ 下一个预制体 + 本类的一个子类，约定预制体名=类名。
/// 预制体根节点挂窗口脚本。
/// 控件绑定：预制体里控件按约定命名（&_image_key_& / &_button_key_& / &_text_key_& /
/// &_gameobject_key_& / $_node_key_$），在 Inspector 点"扫描子节点"（Editor/UIWindowEditor），
/// 组件引用即被序列化进预制体——运行时零 Find/零 GetComponent，代码里 GetButton("key") 按取用。
/// 用法详见 .docs/UI框架-控件引用预序列化.md。
/// 生命周期：OnCreate 只走一次；OnShow 每次打开；OnHide 每次隐藏。
/// 窗口由 UIManager 缓存复用（隐藏不销毁），所以事件的订阅/退订必须在 OnShow/OnHide 里成对出现。
/// </summary>
[DisallowMultipleComponent]
public abstract class UIWindow : MonoBehaviour
{
    // ---- 层级与生命周期（由 UIManager 驱动） ----

    /// <summary>窗口所在层级，默认弹窗；全屏窗、提示覆写本属性。</summary>
    public virtual UIWindowLayer Layer => UIWindowLayer.Popup;

    public bool Opened { get; internal set; }

    public virtual void OnCreate(UIParam param) { } // 只走一次：初始化列表、注册按钮

    public virtual void OnShow(UIParam param) { }   // 每次打开：吃参数、刷显示；订阅事件

    public virtual void OnHide() { }                // 每次隐藏：退订事件、停动画

    // ---- 预序列化引用表（编辑器扫描生成，运行时只做 列表→字典 搬运） ----

    /// <summary>引用条目：key 是代码取用的名字；go 保留 GameObject 引用，组件丢失时降级用。</summary>
    [System.Serializable]
    public class RefEntry
    {
        public string key;

        [SerializeField, HideInInspector] public GameObject go;
    }

    /// <summary>带组件引用的条目：comp 在编辑器扫描时取好并随预制体序列化，运行时不再 GetComponent。</summary>
    [System.Serializable]
    public class RefEntry<T> : RefEntry where T : Component
    {
        public T comp;
    }

    [Header("序列化引用（由编辑器扫描生成，勿手填）")]
    [SerializeField] List<RefEntry<Image>> imageRefs = new List<RefEntry<Image>>();
    [SerializeField] List<RefEntry<Button>> buttonRefs = new List<RefEntry<Button>>();
    [SerializeField] List<RefEntry<TMP_Text>> textRefs = new List<RefEntry<TMP_Text>>();
    [SerializeField] List<RefEntry<UIWindow>> windowRefs = new List<RefEntry<UIWindow>>();
    [SerializeField] List<RefEntry> gameobjectRefs = new List<RefEntry>();

    readonly Dictionary<string, Image> imageDict = new Dictionary<string, Image>();
    readonly Dictionary<string, Button> buttonDict = new Dictionary<string, Button>();
    readonly Dictionary<string, TMP_Text> textDict = new Dictionary<string, TMP_Text>();
    readonly Dictionary<string, UIWindow> windowDict = new Dictionary<string, UIWindow>();
    readonly Dictionary<string, GameObject> gameobjectDict = new Dictionary<string, GameObject>();

    // 不用 Awake 构建：抽象基类的私有 Awake 会被子类自定义 Awake 顶掉导致字典空着，改为取用时懒构建
    bool dictsBuilt;

    /// <summary>把序列化列表搬进运行时字典；幂等，可重复调用。</summary>
    public void BuildDictionaries()
    {
        imageDict.Clear();
        buttonDict.Clear();
        textDict.Clear();
        windowDict.Clear();
        gameobjectDict.Clear();

        FillDict(imageRefs, imageDict);
        FillDict(buttonRefs, buttonDict);
        FillDict(textRefs, textDict);
        FillDict(windowRefs, windowDict);

        foreach (var e in gameobjectRefs)
        {
            if (e == null || string.IsNullOrEmpty(e.key) || e.go == null) continue;
            gameobjectDict[e.key] = e.go;
        }

        dictsBuilt = true;
    }

    // 组件引用有效则进类型字典，否则降级为 GameObject
    void FillDict<T>(List<RefEntry<T>> refs, Dictionary<string, T> dict) where T : Component
    {
        foreach (var e in refs)
        {
            if (e == null || string.IsNullOrEmpty(e.key)) continue;
            if (e.comp != null) dict[e.key] = e.comp;
            else if (e.go != null) gameobjectDict[e.key] = e.go;
        }
    }

    // 编辑器接口：UIWindowEditor 扫描后回写序列化列表
    public void SetImageRefs(List<RefEntry<Image>> list) { imageRefs = list ?? new List<RefEntry<Image>>(); }
    public void SetButtonRefs(List<RefEntry<Button>> list) { buttonRefs = list ?? new List<RefEntry<Button>>(); }
    public void SetTextRefs(List<RefEntry<TMP_Text>> list) { textRefs = list ?? new List<RefEntry<TMP_Text>>(); }
    public void SetWindowRefs(List<RefEntry<UIWindow>> list) { windowRefs = list ?? new List<RefEntry<UIWindow>>(); }
    public void SetGenericRefs(List<RefEntry> list) { gameobjectRefs = list ?? new List<RefEntry>(); }

    /// <summary>按 key 取图片；未扫描过或 key 不存在返回 null。</summary>
    public Image GetImage(string key) { EnsureBuilt(); return imageDict.TryGetValue(key, out var i) ? i : null; }

    /// <summary>按 key 取按钮；未扫描过或 key 不存在返回 null。</summary>
    public Button GetButton(string key) { EnsureBuilt(); return buttonDict.TryGetValue(key, out var b) ? b : null; }

    /// <summary>按 key 取文本（TMP_Text）；未扫描过或 key 不存在返回 null。</summary>
    public TMP_Text GetText(string key) { EnsureBuilt(); return textDict.TryGetValue(key, out var t) ? t : null; }

    /// <summary>按 key 取嵌套子窗口（$_node_key_$ 节点）；未扫描过或 key 不存在返回 null。</summary>
    public UIWindow GetWindow(string key) { EnsureBuilt(); return windowDict.TryGetValue(key, out var w) ? w : null; }

    /// <summary>按 key 取节点（含组件丢失降级的条目）；未扫描过或 key 不存在返回 null。</summary>
    public GameObject GetGameObject(string key) { EnsureBuilt(); return gameobjectDict.TryGetValue(key, out var g) ? g : null; }

    void EnsureBuilt() { if (!dictsBuilt) BuildDictionaries(); }
}
