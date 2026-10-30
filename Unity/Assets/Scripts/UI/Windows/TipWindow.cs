using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// 通用提示窗（M5·T2.1）：tipLayer 过路即走——顶部居中一条消息，淡入→停留→淡出后自动 Hide。
/// 预制体是"空壳"（根节点挂本脚本 + CanvasGroup + 空 UITable），文案节点 OnCreate 代码自建——
/// 不依赖扫描命名，永不缺 key（首个 tipLayer 消费者，兑现 M3 手势表"tip 提示"设计）。
/// 参数（UIParam）：Message=文本；Duration=总时长秒（默认 1.5）；Color=文字颜色（默认白）。
/// 调用形态：UIManager.Instance.Show("TipWindow", param)——窗口开着时重复 Show = 顶掉上一条（重启序列）。
/// 不订阅任何事件、不吃弹窗栈；序列 tween 以本窗口为 Target（OnHide 杀干净，框架动画约定不变）。
/// </summary>
public sealed class TipWindow : UIWindow
{
    public override UIWindowLayer Layer => UIWindowLayer.Tip;

    const float FadeInDuration = 0.12f;
    const float FadeOutDuration = 0.3f;
    const float DefaultDuration = 1.5f;
    const float TopOffsetY = 380f;

    TMP_Text label;
    CanvasGroup group;
    Sequence sequence;

    public override void OnCreate(UIParam param)
    {
        group = GetComponent<CanvasGroup>();

        label = new GameObject("tipText", typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
        RectTransform rect = (RectTransform)label.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, TopOffsetY);
        rect.sizeDelta = new Vector2(1400f, 90f);
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 44f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        if (label.font == null)
            Debug.LogWarning("[TipWindow] TMP 默认字体缺失（先导入 TMP Essentials），提示会不可见");
    }

    public override void OnShow(UIParam param)
    {
        string message = param != null && param.TryGet("Message", out string msg) ? msg : "";
        float duration = param != null && param.TryGet("Duration", out float d) && d > FadeInDuration + FadeOutDuration
            ? d : DefaultDuration;
        Color color = param != null && param.TryGet("Color", out Color c) ? c : Color.white;
        label.text = message;
        label.color = color;

        sequence?.Kill();
        group.alpha = 0f;
        sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
        sequence.Insert(0f, group.DOFade(1f, FadeInDuration).SetEase(Ease.OutQuad));
        sequence.Insert(duration - FadeOutDuration, group.DOFade(0f, FadeOutDuration).SetEase(Ease.InQuad));
        sequence.OnComplete(() => UIManager.Instance.Hide("TipWindow"));
    }

    public override void OnHide()
    {
        sequence?.Kill();
        sequence = null;
        if (group != null) group.alpha = 1f; // 复位干净状态（窗口缓存复用，下次 OnShow 从 0 淡入）
    }
}
