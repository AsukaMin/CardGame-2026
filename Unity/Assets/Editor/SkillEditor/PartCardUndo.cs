using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>
/// 撤销载体（v3.3）：Unity Undo 的快照不捕获 [SerializeReference] 托管引用（实测 6000.3——
/// 卡级普通字段可撤销、Nodes/Edges 内容撤销无效），故图快照改走"普通 string 字段"：
/// token.Blob 恒为上一提交点（=本次操作前）的状态串——操作完成后注册 Undo（此刻快照到的即前态），
/// 再刷新为现态；撤销/重做恢复 Blob → 窗口 OnUndoRedo 里 Apply 写回资产并重建画布。
/// 重做态=撤销瞬间 Unity 捕获的现态串，天然正确。节点是纯字段 POCO，Newtonsoft 序列化（TypeNameHandling.Auto 出多态 $type）。
/// </summary>
public sealed class PartCardUndoToken : ScriptableObject
{
    public string CardPath; // 撤销目标卡（跨卡撤销自动切回）
    public string Blob;     // 图快照 JSON（上一提交点状态；空=无基线，撤销忽略）
}

public static class PartCardUndo
{
    /// <summary>只序列化字段：UnityEngine.Vector2 的 normalized 等只读属性会构成自引用环（实测报
    /// Self referencing loop），而快照契约本就是纯字段 POCO——属性一律不进快照。</summary>
    sealed class FieldsOnlyResolver : DefaultContractResolver
    {
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
            => base.CreateProperties(type, MemberSerialization.Fields);
    }

    static readonly JsonSerializerSettings settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Auto, // List<PartGraphNode> 派生项带 $type
        Formatting = Formatting.None,
        ContractResolver = new FieldsOnlyResolver(),
    };

    [Serializable]
    sealed class Snapshot
    {
        public int CardId;
        public string SlotType;
        public int Cost;
        public string DefaultTarget;
        public string GrantTags;
        public bool Consumable;
        public int Attack;
        public int MaxHp;
        public List<PartGraphNode> Nodes;
        public List<PartGraphEdgeData> Edges;
    }

    /// <summary>卡级字段 + 图（节点/连线）→ JSON 快照串。</summary>
    public static string Capture(PartCardAuthoringAsset asset) => JsonConvert.SerializeObject(new Snapshot
    {
        CardId = asset.CardId,
        SlotType = asset.SlotType,
        Cost = asset.Cost,
        DefaultTarget = asset.DefaultTarget,
        GrantTags = asset.GrantTags,
        Consumable = asset.Consumable,
        Attack = asset.Attack,
        MaxHp = asset.MaxHp,
        Nodes = asset.Nodes,
        Edges = asset.Edges,
    }, settings);

    /// <summary>快照串写回资产（整卡字段+图；撤销/重做落地用）。快照是本工具自产自销，无反序列化类型注入面。</summary>
    public static void Apply(string blob, PartCardAuthoringAsset asset)
    {
        Snapshot snapshot = JsonConvert.DeserializeObject<Snapshot>(blob, settings);
        asset.CardId = snapshot.CardId;
        asset.SlotType = snapshot.SlotType;
        asset.Cost = snapshot.Cost;
        asset.DefaultTarget = snapshot.DefaultTarget;
        asset.GrantTags = snapshot.GrantTags;
        asset.Consumable = snapshot.Consumable;
        asset.Attack = snapshot.Attack;
        asset.MaxHp = snapshot.MaxHp;
        asset.Nodes.Clear();
        asset.Nodes.AddRange(snapshot.Nodes ?? new List<PartGraphNode>());
        asset.Edges.Clear();
        asset.Edges.AddRange(snapshot.Edges ?? new List<PartGraphEdgeData>());
    }
}
