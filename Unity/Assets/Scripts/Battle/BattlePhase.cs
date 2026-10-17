namespace Battle
{
    /// <summary>战斗阶段：线性回合循环的显式状态机（不复用 StateTree——层级能力此处用不上）。</summary>
    public enum BattlePhase
    {
        NotStarted,   // 哨兵值（构造函数会直接开局）
        PlayerMain,   // 玩家行动：出牌/召唤/攻击/弃牌/结束回合
        EnemyTurn,    // 敌方回合：执行意图 → 敌方单位攻击 → 揭示下一意图
        Finished      // 胜负已定
    }

    /// <summary>阵营：玩家 / 敌方。</summary>
    public enum Side { Player, Enemy }

    /// <summary>卡槽四型：身材/入场/常驻/离场。</summary>
    public enum SlotType { Body, OnSummon, Continuous, OnLeave }

    /// <summary>关键词位标记。</summary>
    [System.Flags]
    public enum Keywords { None = 0, Taunt = 1 }

    /// <summary>受修饰的属性。</summary>
    public enum StatType { Attack, MaxHp }

    /// <summary>修饰时长：本回合结束过期 / 持续 N 轮（玩家回合开始递减）/ 战斗内永久。</summary>
    public enum ModifierDurationKind { ThisTurn, Turns, Battle }

    /// <summary>命令失败原因（PlayResult.Reason）。</summary>
    public enum FailReason
    {
        None = 0,
        NotPlayerMain,      // 当前阶段不允许该操作
        BattleFinished,
        CardNotFound,       // 手牌索引越界
        TierNotFound,       // 充能档位不存在
        NotEnoughPoints,    // 行动点不足
        RequiresUnitTarget, // 效果需要指定一个单位目标
        InvalidTarget,      // 目标不存在 / 已死亡 / 阵营不符
        UnitNotFound,
        NotFriendlyUnit,
        AlreadyAttacked,    // 该单位本回合已攻击
        TauntViolation,     // 对方场上有嘲讽单位时只能攻击嘲讽单位
        SleeveNotAvailable, // 该卡套已召唤过或已阵亡
        FieldCapReached     // 场面上限（配置 0=无上限）
    }
}
