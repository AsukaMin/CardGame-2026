using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 控件级 uGUI 事件监听器：把 EventSystem 的全部输入接口（指针、拖拽、滚轮、选中、导航）适配成 C# 事件，
/// 代码里 transform.GetListener().PointDown += ... 直接订阅。与预序列化引用表（UITable）配合：
/// GetGameObject("key").transform.GetListener() 按取用。Button 节点的点击优先用 onClick，其余交互用本组件。
/// 订阅纪律：一次性注册放 OnCreate；动态订阅必须 OnShow/OnHide 成对退订（窗口缓存复用、listener 随节点常驻）。
/// 用法详见 .docs/UI框架-控件事件监听UIEventListener.md。
/// </summary>
public class UIEventListener : MonoBehaviour, IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler,
    IInitializePotentialDragHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    IDropHandler,
    IScrollHandler,
    IUpdateSelectedHandler,
    ISelectHandler,
    IDeselectHandler,
    IMoveHandler,
    ISubmitHandler,
    ICancelHandler
{


    public delegate void PointEventHandler(PointerEventData eventData);
    public delegate void BaseEventHandler(BaseEventData eventData);
    public delegate void AxisEventHandler(AxisEventData eventData);

    public event PointEventHandler PointDown;
    public event PointEventHandler PointUp;
    public event PointEventHandler PointClick;
    public event PointEventHandler Scroll;
    public event PointEventHandler Drop;
    public event PointEventHandler EndDrag;
    public event PointEventHandler Drag;
    public event PointEventHandler BeginDrag;
    public event PointEventHandler InitializePotentialDrag;
    public event PointEventHandler PointExit;
    public event PointEventHandler PointEnter;
    public event BaseEventHandler Cancel;
    public event BaseEventHandler Submit;
    public event BaseEventHandler Deselect;
    public event BaseEventHandler Select;
    public event BaseEventHandler UpdateSelected;
    public event AxisEventHandler Move;



    /// <summary>
    /// 通过transform找事件监听类
    /// </summary>
    /// <param name="tf">按钮（挂载事件监听类的对象）的transform</param>
    /// <returns></returns>
    public static UIEventListener GetListener(Transform tf)
    {
        UIEventListener uiEvent = tf.GetComponent<UIEventListener>();
        if (uiEvent == null) uiEvent = tf.gameObject.AddComponent<UIEventListener>(); // Unity 6 移除了 Component.AddComponent，只能从 GameObject 挂
        return uiEvent;
    }

    /// <summary>
    /// 如果有方法，就执行方法
    /// </summary>
    /// <param name="eventData"></param>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (PointClick != null) PointClick(eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (PointDown != null) PointDown(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (PointUp != null) PointUp(eventData);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (PointEnter != null) PointEnter(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (PointExit != null) PointExit(eventData);
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (InitializePotentialDrag != null) InitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (BeginDrag != null) BeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (Drag != null) Drag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (EndDrag != null) EndDrag(eventData);
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (Drop != null) Drop(eventData);
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (Scroll != null) Scroll(eventData);
    }

    public void OnUpdateSelected(BaseEventData eventData)
    {
        if (UpdateSelected != null) UpdateSelected(eventData);
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (Select != null) Select(eventData);
    }

    public void OnDeselect(BaseEventData eventData)
    {
        if (Deselect != null) Deselect(eventData);
    }

    public void OnMove(AxisEventData eventData)
    {
        if (Move != null) Move(eventData);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (Submit != null) Submit(eventData);
    }

    public void OnCancel(BaseEventData eventData)
    {
        if (Cancel != null) Cancel(eventData);
    }

    /// <summary>
    /// 查找PointClick事件是否有指定的方法
    /// </summary>
    /// <param name="name">方法名</param>
    /// <returns></returns>
    public bool DoesHavePointClick(string name)
    {
        if (PointClick == null) return false;
        foreach (Delegate item in PointClick.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHavePointDown(string name)
    {
        if (PointDown == null) return false;
        foreach (Delegate item in PointDown.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHavePointUp(string name)
    {
        if (PointUp == null) return false;
        foreach (Delegate item in PointUp.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHavePointEnter(string name)
    {
        if (PointEnter == null) return false;
        foreach (Delegate item in PointEnter.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHavePointExit(string name)
    {
        if (PointExit == null) return false;
        foreach (Delegate item in PointExit.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveInitializePotentialDrag(string name)
    {
        if (InitializePotentialDrag == null) return false;
        foreach (Delegate item in InitializePotentialDrag.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveBeginDrag(string name)
    {
        if (BeginDrag == null) return false;
        foreach (Delegate item in BeginDrag.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveDrag(string name)
    {
        if (Drag == null) return false;
        foreach (Delegate item in Drag.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveEndDrag(string name)
    {
        if (EndDrag == null) return false;
        foreach (Delegate item in EndDrag.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveDrop(string name)
    {
        if (Drop == null) return false;
        foreach (Delegate item in Drop.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveScroll(string name)
    {
        if (Scroll == null) return false;
        foreach (Delegate item in Scroll.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveUpdateSelected(string name)
    {
        if (UpdateSelected == null) return false;
        foreach (Delegate item in UpdateSelected.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveSelect(string name)
    {
        if (Select == null) return false;
        foreach (Delegate item in Select.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveDeselect(string name)
    {
        if (Deselect == null) return false;
        foreach (Delegate item in Deselect.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveMove(string name)
    {
        if (Move == null) return false;
        foreach (Delegate item in Move.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveSubmit(string name)
    {
        if (Submit == null) return false;
        foreach (Delegate item in Submit.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

    public bool DoesHaveCancel(string name)
    {
        if (Cancel == null) return false;
        foreach (Delegate item in Cancel.GetInvocationList())
        {
            if (item.Method.Name == name) return true;
        }
        return false;
    }

}

/// <summary>
/// 调用糖：transform.GetListener() 等价 UIEventListener.GetListener(transform)。
/// 扩展方法必须放静态类（MonoBehaviour 类不能直接挂），所以单独成类转发到正式入口。
/// </summary>
public static class UIEventListenerExtensions
{
    public static UIEventListener GetListener(this Transform tf) => UIEventListener.GetListener(tf);

    /// <summary>GameObject 调用糖：UITable.GetGameObject(key) 的返回值直接链式挂监听，省一次 .transform。</summary>
    public static UIEventListener GetListener(this GameObject go) => UIEventListener.GetListener(go.transform);
}
