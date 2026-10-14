using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// 三层 UI 管理器：全屏层互斥（开新顶旧）、弹窗层可叠加（Back 按栈回退）、提示层过路即走。
/// 窗口同步加载 Resources/UI/预制体（约定：预制体名=窗口类名），缓存复用、隐藏不销毁（ET 同款策略）。
/// 全项目只有这一个类知道窗口预制体在哪，其他代码开窗一律 UIManager.I.Show(...)。
/// 用法一：场景里手工搭好三个层节点，拖进下面三个字段。
/// 用法二：什么都不拖，把本组件挂到任意物体上，Awake 自动建 Canvas + EventSystem + 三层。
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager I { get; private set; }

    [SerializeField] Transform fullscreenLayer; // 全屏窗：互斥，开新顶旧
    [SerializeField] Transform popupLayer;      // 弹窗：可叠加，按栈回退
    [SerializeField] Transform tipLayer;        // 提示：过路即走
    [SerializeField] string startWindow;        // 可选：Start 后自动打开的第一扇窗（如 MainMenu）

    readonly Dictionary<string, UIWindow> cache = new Dictionary<string, UIWindow>();
    readonly List<string> popupStack = new List<string>();
    string fullscreenNow; // 当前显示着的全屏窗名

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        if (fullscreenLayer == null) BuildDefaultRoot(); // 没拖引用就自己搭一套
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(startWindow)) Show(startWindow);
    }

    public void Show(string winName, UIParam param = null)
    {
        if (!cache.TryGetValue(winName, out UIWindow win))
        {
            GameObject prefab = Resources.Load<GameObject>("UI/" + winName); // 约定：预制体名=类名
            if (prefab == null)
            {
                Debug.LogError($"[UIManager] 找不到 Resources/UI/{winName}，约定：预制体名=窗口类名");
                return;
            }
            UIWindow sample = prefab.GetComponent<UIWindow>();
            if (sample == null)
            {
                Debug.LogError($"[UIManager] 预制体 {winName} 根节点没挂 UIWindow 子类脚本");
                return;
            }
            win = Instantiate(prefab, LayerOf(sample.Layer)).GetComponent<UIWindow>();
            win.name = winName; // 去掉 (Clone) 后缀
            cache[winName] = win;
        }

        // 全屏窗互斥：开新的先把旧的全屏窗顶掉
        if (win.Layer == UIWindowLayer.Fullscreen)
        {
            if (fullscreenNow != null && fullscreenNow != winName) Hide(fullscreenNow);
            fullscreenNow = winName;
        }

        win.gameObject.SetActive(true);
        if (!win.Opened)
        {
            win.Opened = true;
            win.OnCreate(param);
        }
        win.OnShow(param);

        // 弹窗压栈；已开着的窗口重复 Show = 置顶 + 换参数刷新
        if (win.Layer == UIWindowLayer.Popup)
        {
            popupStack.Remove(winName);
            popupStack.Add(winName);
        }
    }

    public void Hide(string winName)
    {
        if (!cache.TryGetValue(winName, out UIWindow win)) return;
        win.OnHide();
        win.gameObject.SetActive(false); // 缓存复用，不销毁——ET 同款策略
        popupStack.Remove(winName);
        if (fullscreenNow == winName) fullscreenNow = null;
        if (win.Layer == UIWindowLayer.Popup) UiEvents.FireWindowClosed(winName);
    }

    /// <summary>回退：关掉栈顶弹窗（给返回键/关闭按钮接）。</summary>
    public void Back()
    {
        if (popupStack.Count > 0) Hide(popupStack[popupStack.Count - 1]);
    }

    Transform LayerOf(UIWindowLayer layer)
    {
        switch (layer)
        {
            case UIWindowLayer.Fullscreen: return fullscreenLayer;
            case UIWindowLayer.Popup: return popupLayer;
            default: return tipLayer;
        }
    }

    // ---- 以下是"什么都不拖也能跑"的自动搭根，手工搭场景的话可无视 ----

    void BuildDefaultRoot()
    {
        var canvasGo = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // 工程是 Input System only（activeInputHandler=1），必须用 InputSystemUIInputModule
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform, false);
        }

        // 按创建顺序叠放：全屏在最底，弹窗次之，提示最顶
        fullscreenLayer = CreateLayer("FullscreenLayer", canvasGo.transform);
        popupLayer = CreateLayer("PopupLayer", canvasGo.transform);
        tipLayer = CreateLayer("TipLayer", canvasGo.transform);
    }

    static Transform CreateLayer(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).transform as RectTransform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }
}
