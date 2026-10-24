using System;
using Battle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 手牌条目视图（M3）：实例化后运行时 AddComponent 到 CardItem 根上（模板预制体按约定只挂 UITable，不放脚本）。
/// 控件 key 与 CardItem.prefab 根 UITable 扫描结果对应：
/// name/cost/attack/hp（效果卡攻血留空）/drag（手势热区）/bg（边框，随卡型切换）。
/// 拖拽=纯手势输入：drag 节点经 UIEventListener 转发 EndDrag 给窗口，由窗口判定释放点并调命令（策划案§六）。
/// </summary>
public sealed class CardItemView : MonoBehaviour
{
    public BattleCard Card { get; private set; }
    public int HandIndex { get; private set; }

    UITable table;
    Action<CardItemView, PointerEventData> onDragEnd;

    public void Setup(BattleCard card, int handIndex, Action<CardItemView, PointerEventData> dragEndHandler)
    {
        Card = card;
        HandIndex = handIndex;
        onDragEnd = dragEndHandler;
        table = GetComponent<UITable>();

        SetText("name", card.Name);
        SetText("cost", card.Cost.ToString());
        SetText("attack", card.Monster != null ? card.Monster.Attack.ToString() : "");
        SetText("hp", card.Monster != null ? card.Monster.MaxHp.ToString() : "");

        Image bg = table != null ? table.GetImage("bg") : null;
        if (bg != null)
        {
            Sprite frame = FrameOf(card.IsMonster);
            if (frame != null) bg.sprite = frame; // 取不到（如打包版）保持预制体默认图
        }

        if (onDragEnd == null) return;
        GameObject dragNode = table != null ? table.GetGameObject("drag") : null;
        if (dragNode == null)
        {
            Debug.LogError($"[CardItemView] 「{card.Name}」缺 drag 节点（key=drag）——CardItem.prefab 未按约定命名/扫描");
            return;
        }
        dragNode.GetListener().EndDrag += eventData => onDragEnd(this, eventData);
    }

    /// <summary>
    /// 边框图取用。临时方案：写死路径经 AssetDatabase 直载（编辑器期专用 API，打包版返回 null，
    /// 边框保持预制体默认图）；正式版按规划换配置表配路径（Loader 方案）。
    /// </summary>
    static Sprite FrameOf(bool isMonster)
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(isMonster
            ? "Assets/Art/UI素材/card_bg_creature.png"
            : "Assets/Art/UI素材/card_bg.png");
#else
        return null;
#endif
    }

    void SetText(string key, string value)
    {
        TMP_Text t = table != null ? table.GetText(key) : null;
        if (t != null) t.text = value;
    }
}
