namespace Fusion
{
    /// <summary>卡槽四型：身材/入场/常驻/离场。</summary>
    public enum SlotType { Body, OnSummon, Continuous, OnLeave }

    /// <summary>关键词位标记。</summary>
    [System.Flags]
    public enum Keywords { None = 0, Taunt = 1 }

    /// <summary>受修饰的属性。</summary>
    public enum StatType { Attack, MaxHp }

    /// <summary>修饰时长词汇（纯数据形状）：本回合结束过期 / 持续 N 轮 / 整场永久——语义由未来战斗核心绑定。</summary>
    public enum ModifierDurationKind { ThisTurn, Turns, Fusion }
}
