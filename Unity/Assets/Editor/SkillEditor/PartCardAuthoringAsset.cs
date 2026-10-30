using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 部位卡作者稿（M5·T5' 技能编辑器的唯一事实源）：图结构（节点+连线）+ 布局坐标 + 卡级字段。
/// 纪律（M2 延伸）：导出的 parts.json 不手改，回编辑器改再导出；坐标只活在本层，永不进数据格式。
/// 每卡一个 .asset（文件名=卡名，决策定案 2026-10-28）；节点实例经 [SerializeReference] 多态序列化。
/// </summary>
[CreateAssetMenu(fileName = "新部位卡", menuName = "SkillEditor/部位卡作者稿", order = 0)]
public sealed class PartCardAuthoringAsset : ScriptableObject
{
    [Header("卡级属性（技能节点承载——需求文档§5.2）")]
    [Tooltip("卡牌表行 id（同类型现有最大 +1 自动分配；导出重复 id 中止）")]
    public int CardId;
    [Tooltip("槽型：Body=身材 / OnSummon=入场 / Continuous=常驻 / OnLeave=离场")]
    public string SlotType = "OnSummon";
    [Tooltip("写死的费用（槽内卡不充能，召唤时一次付清）")]
    public int Cost = 1;
    [Tooltip("技能目标：链的起始作用对象（链上不切换时全部作用于此）")]
    public string DefaultTarget = "Self";
    [Tooltip("随召唤赋予宿主单位的标签名，逗号分隔（如 Keyword.Taunt）")]
    public string GrantTags = "";
    [Tooltip("消耗标记：死亡随包销毁，战后不回收")]
    public bool Consumable;
    [Tooltip("攻击底数（仅身材槽）")]
    public int Attack;
    [Tooltip("生命底数（仅身材槽）")]
    public int MaxHp = 1;

    [Header("图（节点+连线；布局坐标在节点数据里）")]
    public List<PartGraphNode> Nodes = new List<PartGraphNode>();
    public List<PartGraphEdgeData> Edges = new List<PartGraphEdgeData>();
}

// ==================== 作者稿节点（纯 POCO，不依赖 GraphView——换画布不动数据） ====================

/// <summary>图节点基类：稳定 Guid（连线引用）+ 布局坐标。</summary>
[Serializable]
public abstract class PartGraphNode
{
    public string Guid = System.Guid.NewGuid().ToString("N");
    public Vector2 Position;
}

/// <summary>操作种类（M5 集=现有 Battle 词汇；「获得行动点」定案不做，M6 立项后再加）。</summary>
public enum OpNodeKind { Damage, Heal, ModifyAttribute, DrawCards, GrantTag, Condition }

/// <summary>节点目标三选一（需求文档§4.4）：父传入=沿用当前组 / 施法者=开新组(Self) / 技能主目标=开新组(Chosen)。</summary>
public enum NodeTargetScope { ParentPassed, Caster, Chosen }

/// <summary>技能主节点（根）的输出端口——按槽型生成（需求文档§6.2 v1.1：常驻=双端口）。</summary>
public static class RootPorts
{
    public const string OnSummon = "root:OnSummon";                 // 入场·立即
    public const string OnSummonLate = "root:OnSummonLate";         // 入场·动画完成时（PlayAfterAnimation）
    public const string ContinuousTrigger = "root:ContinuousTrigger"; // 常驻·触发（链首=触发配置节点）
    public const string ContinuousAura = "root:ContinuousAura";     // 常驻·光环（白名单：属性修饰/赋予关键词）
    public const string OnLeave = "root:OnLeave";                   // 离场·死亡时

    /// <summary>槽型适用的端口（身材槽无链端口——数值直填）。</summary>
    public static string[] Of(string slotType)
    {
        switch (slotType)
        {
            case "OnSummon": return new[] { OnSummon, OnSummonLate };
            case "Continuous": return new[] { ContinuousTrigger, ContinuousAura };
            case "OnLeave": return new[] { OnLeave };
            default: return Array.Empty<string>();
        }
    }
}

/// <summary>操作节点：一种操作 + 参数位（按 Kind 取用）。</summary>
[Serializable]
public sealed class OpNodeData : PartGraphNode
{
    public OpNodeKind Kind = OpNodeKind.Damage;
    public NodeTargetScope Target = NodeTargetScope.ParentPassed;
    // —— 通用数值位 ——
    public double Amount = 1;                    // Damage / Heal
    public string Attribute = "CurrentHp";       // ModifyAttribute：CurrentHp/MaxHp/Attack/Shield
    public string ModOp = "Add";                 // Add/Multiply/Override
    public double Magnitude = 1;                 // ModifyAttribute
    public string Duration = "Permanent";        // Permanent/Rounds/ThisPhase（光环链忽略——光环恒随生命周期）
    public int Rounds = 1;
    public int Count = 1;                        // DrawCards
    public string Tag = "";                      // GrantTag
    // —— 条件（Kind==Condition）——
    public string CondAttribute = "CurrentHp";
    public string CondOp = "LessOrEqual";
    public string CondValueType = "Fixed";       // Fixed/PercentOfMaxHp
    public double CondValue;
    public string CondSubject = "Current";       // Current/Source/MainTarget（求值对象三选）
}

/// <summary>选择目标节点（纯结构节点）：把后续链切进新目标组。</summary>
[Serializable]
public sealed class TargetSwitchNodeData : PartGraphNode
{
    public string TargetMode = "Self";           // TargetMode 枚举名
}

/// <summary>触发配置节点（常驻「触发」链首专用）：盯什么事件、何时触发。</summary>
[Serializable]
public sealed class TriggerConfigNodeData : PartGraphNode
{
    public string EventTag = "Event.Turn.Started";
    public bool SelfOnly = true;
    public bool SourceOnly;
    public bool Once;
    public bool HasValueFilter;                  // "数值 ≥N 才触发"
    public double ValueFilterMin = 1;
}

/// <summary>连线：From = 端口键（root:…）或节点 Guid 或条件出口（Guid:yes / Guid:no）；To = 节点 Guid（输入单进）。</summary>
[Serializable]
public sealed class PartGraphEdgeData
{
    public string From;
    public string To;
}

/// <summary>
/// 编辑器中文标签表（M5 验收反馈）：数据侧一律存英文枚举名（parts.json 合同不动），显示层翻译。
/// 值数组与名数组按下标一一对应；窗口下拉与画布节点摘要共用本表。
/// </summary>
public static class PartCardLabels
{
    public static readonly string[] SlotValues = { "Body", "OnSummon", "Continuous", "OnLeave" };
    public static readonly string[] SlotNames = { "身材槽", "入场槽", "常驻槽", "离场槽" };

    public static readonly string[] TargetValues =
        { "Chosen", "Self", "AllAlliedUnits", "AllOpposingUnits", "AllUnits", "RandomOpposingUnit", "AlliedLeader", "OpposingLeader", "Instigator", "EventTarget" };
    public static readonly string[] TargetNames =
        { "玩家选择的目标", "自身", "我方全体单位", "敌方全体单位", "双方全体单位", "随机敌方单位", "我方主战者", "敌方主战者", "事件引发者", "事件承受者" };

    public static readonly string[] AttributeValues = { "Attack", "CurrentHp", "MaxHp", "Shield" };
    public static readonly string[] AttributeNames = { "攻击", "当前生命", "生命上限", "护盾" };

    public static readonly string[] ModValues = { "Add", "Multiply", "Override" };
    public static readonly string[] ModNames = { "加算", "乘算", "覆盖" };

    public static readonly string[] DurationValues = { "Permanent", "Rounds", "ThisPhase" };
    public static readonly string[] DurationNames = { "永久", "按回合", "本相位" };

    public static readonly string[] CompareValues = { "Greater", "GreaterOrEqual", "Less", "LessOrEqual", "Equal", "NotEqual" };
    public static readonly string[] CompareNames = { "大于", "大于等于", "小于", "小于等于", "等于", "不等于" };

    public static readonly string[] ValueTypeValues = { "Fixed", "PercentOfMaxHp" };
    public static readonly string[] ValueTypeNames = { "固定值", "生命上限百分比" };

    public static readonly string[] SubjectValues = { "Current", "Source", "MainTarget" };
    public static readonly string[] SubjectNames = { "当前目标", "施法者", "技能主目标" };

    public static readonly string[] EventTagValues =
        { "Event.Turn.Started", "Event.Damage.Applied", "Event.Damage.Preview", "Event.Unit.Summoned", "Event.Unit.Died", "Event.Card.Drawn" };
    public static readonly string[] EventTagNames =
        { "回合开始", "伤害结算后", "伤害预览（事前）", "单位召唤上场", "单位死亡", "抽到卡" };

    public static readonly string[] NodeScopeValues = { "ParentPassed", "Caster", "Chosen" };
    public static readonly string[] NodeScopeNames = { "父节点传入", "施法者", "技能主目标" };

    public static readonly string[] OpValues = { "Damage", "Heal", "ModifyAttribute", "DrawCards", "GrantTag", "Condition" };
    public static readonly string[] OpNames = { "伤害", "治疗", "属性修饰", "抽牌", "赋予关键词", "条件分支" };

    /// <summary>值 → 显示名（未知值原样返回——数据兼容）。</summary>
    public static string Display(string[] values, string[] names, string value)
    {
        int index = value == null ? -1 : Array.IndexOf(values, value);
        return index >= 0 ? names[index] : value;
    }

    public static string Slot(string value) => Display(SlotValues, SlotNames, value);
    public static string Target(string value) => Display(TargetValues, TargetNames, value);
    public static string Attribute(string value) => Display(AttributeValues, AttributeNames, value);
    public static string Mod(string value) => Display(ModValues, ModNames, value);
    public static string Duration(string value) => Display(DurationValues, DurationNames, value);
    public static string EventTag(string value) => Display(EventTagValues, EventTagNames, value);
}
