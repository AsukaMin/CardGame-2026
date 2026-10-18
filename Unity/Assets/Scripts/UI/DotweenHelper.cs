using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 动画工具（DOTween 封装）：弹窗弹开/收起、背景遮罩、数字滚动、飘字。
/// 纯手动调用，不接进 UIManager——窗口自己在 OnShow / 关闭按钮 / OnHide 里调：
/// OnShow 弹开、关闭按钮"先动画再 Hide"、OnHide 里 KillWindowTweens 兜底停动画。
/// 约定：所有动画 SetUpdate(true)（unscaled time，暂停菜单里照播）；窗口相关 tween
/// 一律以窗口对象为 Target，OnHide 里 KillWindowTweens(win) 一次杀干净。
/// 用法详见 .docs/UI框架-窗口动画DotweenHelper.md。
/// </summary>
public static class DotweenHelper
{
    // ---- 默认值（调用处可用参数覆盖） ----

    public const float PopOpenDuration = 0.25f; // 弹开时长
    public const float PopCloseDuration = 0.2f; // 收起时长（比弹开略快，关东西要干脆）
    const float PopScale = 0.85f;               // 弹开起点 / 收起终点缩放
    const float FadeRatio = 0.6f;               // 淡入淡出时长占弹收时长的比例（先透后实，手感更弹）
    const float DimAlpha = 0.45f;               // 遮罩目标透明度
    const float FloatRise = 60f;                // 飘字上浮距离

    // 每个弹窗的遮罩：窗口 → 遮罩 Image（PopOpen 创建，随关闭淡出后销毁）
    static readonly Dictionary<UIWindow, Image> dims = new Dictionary<UIWindow, Image>();

    // ---- 弹窗 ----

    /// <summary>
    /// 弹窗弹开：缩放 0.85→1（OutBack）+ CanvasGroup 淡入（没有会自动补挂），动画期间不可交互。
    /// withDim=true 时在窗口正下方垫一张全屏黑色遮罩（模态：挡住下层点击）随窗淡入；
    /// 传 onDimClick 则遮罩可点击（典型写法：() => UIManager.I.Back()）。返回 Sequence 供续接。
    /// </summary>
    public static Sequence PopOpen(UIWindow win, bool withDim = true, Action onDimClick = null,
                                   float duration = PopOpenDuration, float dimAlpha = DimAlpha)
    {
        CanvasGroup cg = EnsureCanvasGroup(win);
        KillWindowTweens(win); // 关闭中重开等竞态：先杀旧动画、清遮罩、复位状态

        RectTransform rect = (RectTransform)win.transform;
        cg.alpha = 0f;
        cg.interactable = false; // 动画期间防误点，播完解锁
        rect.localScale = Vector3.one * PopScale;

        if (withDim) CreateDim(win, onDimClick, dimAlpha, duration);

        Sequence seq = DOTween.Sequence().SetUpdate(true).SetTarget(win);
        seq.Insert(0f, rect.DOScale(Vector3.one, duration).SetEase(Ease.OutBack));
        seq.Insert(0f, cg.DOFade(1f, duration * FadeRatio).SetEase(Ease.OutQuad));
        seq.OnComplete(() => cg.interactable = true);
        return seq;
    }

    /// <summary>
    /// 弹窗收起：缩放+淡出，播完把窗口复位成干净状态（缓存复用的窗口下次打开才不会歪），
    /// 然后回调 onComplete——典型写法：PopClose(this, () => UIManager.I.Hide(nameof(XxxWindow)))。
    /// 时序是"先动画、动画完了再 Hide"，UIManager.Hide 本身保持瞬时不动。
    /// </summary>
    public static Sequence PopClose(UIWindow win, Action onComplete = null, float duration = PopCloseDuration)
    {
        CanvasGroup cg = EnsureCanvasGroup(win);
        DOTween.Kill(win); // 杀掉可能还在播的打开动画，从当前状态开始收
        FadeOutDim(win, duration);

        RectTransform rect = (RectTransform)win.transform;
        cg.interactable = false; // 收起期间防连点

        Sequence seq = DOTween.Sequence().SetUpdate(true).SetTarget(win);
        seq.Insert(0f, rect.DOScale(Vector3.one * PopScale, duration).SetEase(Ease.InBack));
        seq.Insert(0f, cg.DOFade(0f, duration * FadeRatio).SetEase(Ease.InQuad));
        seq.OnComplete(() =>
        {
            ResetState(win);
            onComplete?.Invoke();
        });
        return seq;
    }

    /// <summary>
    /// OnHide 里调用（框架"停动画"约定的落地）：杀掉窗口全部 tween、瞬间移除遮罩、复位状态。
    /// 覆盖绕过关闭动画的路径：Back() 栈回退、全屏窗开新顶旧。
    /// </summary>
    public static void KillWindowTweens(UIWindow win)
    {
        if (win == null) return;
        DOTween.Kill(win);
        RemoveDimInstant(win);
        ResetState(win);
    }

    // ---- 遮罩（弹窗模态背景） ----

    static void CreateDim(UIWindow win, Action onDimClick, float dimAlpha, float duration)
    {
        RemoveDimInstant(win); // 防御：不该有旧遮罩，有就先清掉

        Transform layer = win.transform.parent;
        if (layer == null)
        {
            Debug.LogWarning($"[DotweenHelper] {win.name} 不在 UI 层节点下，跳过遮罩");
            return;
        }

        var go = new GameObject(win.name + "_Dim", typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(layer, false);
        rect.SetSiblingIndex(win.transform.GetSiblingIndex()); // 垫在窗口正下方、旧窗口上方
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        Image img = go.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true; // 模态：挡住对下层窗口的点击

        if (onDimClick != null)
        {
            Button btn = go.AddComponent<Button>();
            btn.transition = Button.Transition.None;
            btn.onClick.AddListener(() => onDimClick());
        }

        dims[win] = img;
        img.DOFade(dimAlpha, duration).SetUpdate(true).SetTarget(win).SetEase(Ease.OutQuad);
    }

    static void FadeOutDim(UIWindow win, float duration)
    {
        if (!dims.TryGetValue(win, out Image img)) return;
        if (img == null) { dims.Remove(win); return; }
        DOTween.Kill(img);
        img.DOFade(0f, duration).SetUpdate(true).SetEase(Ease.InQuad).OnComplete(() =>
        {
            if (dims.TryGetValue(win, out Image cur) && cur == img) dims.Remove(win);
            UnityEngine.Object.Destroy(img.gameObject);
        });
    }

    static void RemoveDimInstant(UIWindow win)
    {
        if (!dims.TryGetValue(win, out Image img)) return;
        dims.Remove(win);
        if (img == null) return;
        DOTween.Kill(img);
        UnityEngine.Object.Destroy(img.gameObject);
    }

    // ---- 数字滚动与飘字 ----

    /// <summary>
    /// 数字滚动：把 TMP 文本从 from 插值滚到 to（HP、金币……）。
    /// 同一 label 重复调用自动顶掉上一次滚动；format 传 "0" / "F1" 等标准格式串。
    /// </summary>
    public static Tween NumberRoll(TMP_Text label, float from, float to,
                                   float duration = 0.5f, string format = "0")
    {
        DOTween.Kill(label); // 顶掉同 label 上一次滚动
        float cur = from;
        return DOTween.To(() => cur, v =>
               {
                   cur = v;
                   label.SetText(v.ToString(format, CultureInfo.InvariantCulture));
               }, to, duration)
                      .SetUpdate(true).SetTarget(label).SetEase(Ease.OutQuad);
    }

    /// <summary>
    /// 通用飘字：在 parent（Canvas 内任意 RectTransform）下纯代码建一个临时 TMP 文本，
    /// 原地上浮+淡出后自毁，不依赖预制体。anchoredPos 是相对 parent 的锚点坐标。
    /// 战斗世界空间的飘字（头顶血条那种）等战斗核心重写后再扩展。
    /// </summary>
    public static Tween FloatText(Transform parent, Vector2 anchoredPos, string content,
                                  Color? color = null, float duration = 0.8f, float fontSize = 36f)
    {
        var go = new GameObject("FloatText", typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(300f, 60f);

        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.color = color ?? Color.white;
        label.text = content;
        if (label.font == null)
            Debug.LogWarning("[DotweenHelper] TMP 默认字体缺失（先导入 TMP Essentials），飘字会不可见");

        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        Sequence seq = DOTween.Sequence().SetUpdate(true);
        seq.Insert(0f, rect.DOAnchorPos(anchoredPos + Vector2.up * FloatRise, duration).SetEase(Ease.OutQuad));
        seq.Insert(duration * 0.4f, cg.DOFade(0f, duration * 0.6f).SetEase(Ease.InQuad)); // 前半程停留，后半程淡出
        seq.OnKill(() => { if (go != null) UnityEngine.Object.Destroy(go); }); // 正常播完或中途被杀都不留尸体
        return seq;
    }

    // ---- 内部 ----

    static CanvasGroup EnsureCanvasGroup(UIWindow win)
    {
        CanvasGroup cg = win.GetComponent<CanvasGroup>();
        return cg != null ? cg : win.gameObject.AddComponent<CanvasGroup>();
    }

    // 窗口缓存复用：动画结束后必须还原成干净状态，否则下次打开带着上次的缩放/透明度
    static void ResetState(UIWindow win)
    {
        ((RectTransform)win.transform).localScale = Vector3.one;
        CanvasGroup cg = win.GetComponent<CanvasGroup>(); // 只取不补：没弹过的窗口不该被强加组件
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
    }
}
