using System;
using System.Collections.Generic;
using Battle;
using UnityEngine;

namespace CardAuthoring
{
    /// <summary>
    /// 部位卡编辑源资产（SO）：与 EffectCardAsset 同构但无档位（整卡一张图）。
    /// 槽型/攻血/费用/关键词/消耗标记属于"数值与结构不可分"的部分，归编辑器；展示元数据归表格。
    /// </summary>
    [CreateAssetMenu(fileName = "NewPartCard", menuName = "CardAuthoring/部位卡")]
    public class PartCardAsset : ScriptableObject
    {
        public int id;

        [Tooltip("编辑期工作名，仅编辑器显示；正式名称归表格（按 id 关联）")]
        public string cardName = "新部位卡";

        [Tooltip("槽型声明，须与卡套槽位匹配才能装入")]
        public SlotType slotType = SlotType.Body;
        public int cost = 1;
        public int attack;
        public int maxHp = 1;
        public Keywords grantKeywords = Keywords.None;

        [Tooltip("消耗标记：单位死亡时随包销毁，战后不回收")]
        public bool consumable;

        public List<EffectDefNodeData> effectDefs = new List<EffectDefNodeData>();
        public List<OpNodeData> opNodes = new List<OpNodeData>();
        public List<ConnectionData> connections = new List<ConnectionData>();
    }
}
