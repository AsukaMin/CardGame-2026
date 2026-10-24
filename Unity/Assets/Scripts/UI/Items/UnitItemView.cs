using System;
using Battle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 场上单位条目视图（M3）：实例化后运行时 AddComponent 到 UnitItem 根上（模板预制体按约定只挂 UITable，不放脚本）。
/// 我方/敌方通用：name/hp(当前)/attack；仅我方单位注册拖拽（攻击手势）——
/// 敌方单位不挂手势，其节点仅作效果卡/攻击的释放判定射线热区（策划案§5.1）。怪兽无盾，不设盾位。
/// </summary>
public sealed class UnitItemView : MonoBehaviour
{
    public SkillEntity Entity { get; private set; }
    public bool IsPlayerSide { get; private set; }

    UITable table;

    public void Setup(SkillEntity entity, bool isPlayerSide, Action<UnitItemView, PointerEventData> dragEndHandler)
    {
        Entity = entity;
        IsPlayerSide = isPlayerSide;
        table = GetComponent<UITable>();

        SetText("name", entity.Name);
        SetText("hp", entity.Attributes.CurrentInt(AttributeId.CurrentHp).ToString());
        SetText("attack", entity.Attributes.CurrentInt(AttributeId.Attack).ToString());

        if (dragEndHandler == null) return;
        GameObject dragNode = table != null ? table.GetGameObject("drag") : null;
        if (dragNode == null)
        {
            Debug.LogError($"[UnitItemView] 「{entity.Name}」缺 drag 节点（key=drag）——UnitItem.prefab 未按约定命名/扫描");
            return;
        }
        dragNode.GetListener().EndDrag += eventData => dragEndHandler(this, eventData);
    }

    void SetText(string key, string value)
    {
        TMP_Text t = table != null ? table.GetText(key) : null;
        if (t != null) t.text = value;
    }
}
