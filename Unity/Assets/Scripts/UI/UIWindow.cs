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
/// 预制体根节点挂窗口子类脚本。
/// 控件绑定走预序列化引用表，载体是同节点的 UITable 组件（RequireComponent 自动带上）：
/// 预制体里控件按约定命名（&_image_key_& / &_button_key_& / &_text_key_& / &_gameobject_key_& /
/// $_node_key_$ / $_table_key_$），在该节点的 UITable 组件上点"扫描子节点"（Editor/UITableEditor），
/// 组件引用即被序列化进预制体——运行时零 Find/零 GetComponent，代码里 GetButton("key") 按取用。
/// 用法详见 .docs/UI框架-控件引用预序列化.md。
/// 生命周期：OnCreate 只走一次；OnShow 每次打开；OnHide 每次隐藏。
/// 窗口由 UIManager 缓存复用（隐藏不销毁），所以事件的订阅/退订必须在 OnShow/OnHide 里成对出现。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UITable))]
public abstract class UIWindow : MonoBehaviour
{
    // ---- 层级与生命周期（由 UIManager 驱动） ----

    /// <summary>窗口所在层级，默认弹窗；全屏窗、提示覆写本属性。</summary>
    public virtual UIWindowLayer Layer => UIWindowLayer.Popup;

    public bool Opened { get; internal set; }

    public virtual void OnCreate(UIParam param) { } // 只走一次：初始化列表、注册按钮

    public virtual void OnShow(UIParam param) { }   // 每次打开：吃参数、刷显示；订阅事件

    public virtual void OnHide() { }                // 每次隐藏：退订事件、停动画

    // ---- 预序列化引用表（实际数据与实现都在同节点的 UITable 组件上） ----

    UITable table;

    /// <summary>本窗口的预序列化引用表；UITable 也可脱离窗口单独挂在列表项/小部件预制体上用。</summary>
    public UITable Table
    {
        get { if (table == null) table = GetComponent<UITable>(); return table; }
    }

    // ---- 快捷转发（窗口子类直接 GetButton("key")，等价 Table.GetButton("key")；找不到返回 null） ----

    public Image GetImage(string key) => Table.GetImage(key);

    public Button GetButton(string key) => Table.GetButton(key);

    public TMP_Text GetText(string key) => Table.GetText(key);

    public UIWindow GetWindow(string key) => Table.GetWindow(key);

    public UITable GetTable(string key) => Table.GetTable(key);

    public GameObject GetGameObject(string key) => Table.GetGameObject(key);
}
