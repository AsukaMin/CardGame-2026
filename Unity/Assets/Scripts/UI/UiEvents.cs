using System;

/// <summary>
/// UI 自己的事：弹窗关闭、红点变化。
/// 弹窗关闭由 UIManager.Hide 统一发；红点等其他窗口间全局广播按需补。
/// </summary>
public static class UiEvents
{
    public static event Action<string> OnWindowClosed;          // 被关掉的弹窗名
    public static event Action<string, bool> OnRedPointChanged; // (红点key, 是否亮)

    public static void FireWindowClosed(string winName) => OnWindowClosed?.Invoke(winName);
    public static void FireRedPointChanged(string key, bool on) => OnRedPointChanged?.Invoke(key, on);
}
