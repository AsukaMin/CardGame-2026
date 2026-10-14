using UnityEngine;

/// <summary>窗口层级，决定 UIManager 把窗口实例化到哪一层（子类用 Layer 属性返回）。</summary>
public enum UIWindowLayer
{
    Fullscreen, // 全屏窗：互斥，开新顶旧
    Popup,      // 弹窗：可叠加，按栈回退
    Tip,        // 提示：过路即走，不进栈
}

/// <summary>
/// 窗口基类。每个窗口 = Resources/UI/ 下一个预制体 + 本类的一个子类，约定预制体名=类名。
/// 预制体根节点挂窗口脚本，控件引用一律 [SerializeField] 在 Inspector 里拖——
/// 不做名字字典绑定（UIObjs 那套重名/空条目问题整体不存在）。
/// 生命周期：OnCreate 只走一次；OnShow 每次打开；OnHide 每次隐藏。
/// 窗口由 UIManager 缓存复用（隐藏不销毁），所以事件的订阅/退订必须在 OnShow/OnHide 里成对出现。
/// </summary>
public abstract class UIWindow : MonoBehaviour
{
    /// <summary>窗口所在层级，默认弹窗；全屏窗、提示覆写本属性。</summary>
    public virtual UIWindowLayer Layer => UIWindowLayer.Popup;

    public bool Opened { get; internal set; }

    public virtual void OnCreate(UIParam param) { } // 只走一次：初始化列表、注册按钮

    public virtual void OnShow(UIParam param) { }   // 每次打开：吃参数、刷显示；订阅事件

    public virtual void OnHide() { }                // 每次隐藏：退订事件、停动画
}
