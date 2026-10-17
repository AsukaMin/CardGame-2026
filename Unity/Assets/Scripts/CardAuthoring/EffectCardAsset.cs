using System;
using System.Collections.Generic;
using UnityEngine;

namespace CardAuthoring
{
    /// <summary>
    /// 效果卡编辑源资产（SO）：编辑期物料，不进包、不走 Resources——运行时数据由 CardPoolExporter
    /// 编译导出到 GameData/effect_cards.json。正式名称/描述/卡面归表格（按 id 关联），此处只存工作名。
    /// </summary>
    [CreateAssetMenu(fileName = "NewEffectCard", menuName = "CardAuthoring/效果卡")]
    public class EffectCardAsset : ScriptableObject
    {
        public int id;

        [Tooltip("编辑期工作名，仅编辑器显示；正式名称归表格（按 id 关联）")]
        public string cardName = "新效果卡";

        /// <summary>一个充能档位 = 一张独立的图（费用/标签 + 效果组/节点/连线）。</summary>
        [Serializable]
        public class TierData
        {
            public int cost = 2;
            public string label = "低配";
            public List<EffectDefNodeData> effectDefs = new List<EffectDefNodeData>();
            public List<OpNodeData> opNodes = new List<OpNodeData>();
            public List<ConnectionData> connections = new List<ConnectionData>();
        }

        public List<TierData> tiers = new List<TierData> { new TierData() };
    }
}
