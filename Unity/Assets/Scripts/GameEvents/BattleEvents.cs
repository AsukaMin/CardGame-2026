namespace GameEvents
{
    // ==================== 战斗事实事件（M1 战斗核心广播，表现层只监听） ====================
    // 约定：readonly struct、名词过去时、载荷字段最小化（详见《事件系统设计》§2）。
    // 发送方：Battle.BattleSession（结算完成后的安全点）；逻辑核心不订阅本总线。

    /// <summary>战斗开始（双方主战者满血入场）。</summary>
    public readonly struct BattleStarted
    {
        public readonly int PlayerMaxHp;
        public readonly int EnemyMaxHp;
        public BattleStarted(int playerMaxHp, int enemyMaxHp)
        {
            PlayerMaxHp = playerMaxHp;
            EnemyMaxHp = enemyMaxHp;
        }
    }

    /// <summary>一方回合开始。出牌阶段与该方回合开始重合（M1 唯一子阶段）。</summary>
    public readonly struct TurnStarted
    {
        public readonly int Turn;
        public readonly bool IsPlayerSide;
        public TurnStarted(int turn, bool isPlayerSide)
        {
            Turn = turn;
            IsPlayerSide = isPlayerSide;
        }
    }

    /// <summary>玩家能量变化（获得/消耗统一走此事件）。</summary>
    public readonly struct EnergyChanged
    {
        public readonly int Before;
        public readonly int After;
        public EnergyChanged(int before, int after)
        {
            Before = before;
            After = after;
        }
    }

    /// <summary>抽到一张牌（牌库空跳过时不发）。</summary>
    public readonly struct CardDrawn
    {
        public readonly string CardName;
        public CardDrawn(string cardName) => CardName = cardName;
    }

    /// <summary>效果卡打出（结算前发；后续伤害见 DamageResolved）。</summary>
    public readonly struct CardPlayed
    {
        public readonly string CardName;
        public readonly int TargetId;
        public CardPlayed(string cardName, int targetId)
        {
            CardName = cardName;
            TargetId = targetId;
        }
    }

    /// <summary>怪兽召唤上场。</summary>
    public readonly struct MonsterSummoned
    {
        public readonly int UnitId;
        public readonly string Name;
        public readonly int Attack;
        public readonly int MaxHp;
        public readonly bool IsPlayerSide;
        public MonsterSummoned(int unitId, string name, int attack, int maxHp, bool isPlayerSide)
        {
            UnitId = unitId;
            Name = name;
            Attack = attack;
            MaxHp = maxHp;
            IsPlayerSide = isPlayerSide;
        }
    }

    /// <summary>一次攻击结算完毕（战斗伤害；Amount=实际落地值）。</summary>
    public readonly struct AttackResolved
    {
        public readonly int AttackerId;
        public readonly string AttackerName;
        public readonly int TargetId;
        public readonly string TargetName;
        public readonly int Amount;
        public AttackResolved(int attackerId, string attackerName, int targetId, string targetName, int amount)
        {
            AttackerId = attackerId;
            AttackerName = attackerName;
            TargetId = targetId;
            TargetName = targetName;
            Amount = amount;
        }
    }

    /// <summary>一次效果伤害落地（法术来源；战斗伤害走 AttackResolved）。</summary>
    public readonly struct DamageResolved
    {
        public readonly int SourceId;
        public readonly string SourceName;
        public readonly int TargetId;
        public readonly string TargetName;
        public readonly int Amount;
        public readonly int TargetHpAfter;
        public DamageResolved(int sourceId, string sourceName, int targetId, string targetName,
            int amount, int targetHpAfter)
        {
            SourceId = sourceId;
            SourceName = sourceName;
            TargetId = targetId;
            TargetName = targetName;
            Amount = amount;
            TargetHpAfter = targetHpAfter;
        }
    }

    /// <summary>单位死亡离场（主战者不死亡——死亡即终局，见 BattleEnded）。</summary>
    public readonly struct UnitDied
    {
        public readonly int UnitId;
        public readonly string Name;
        public UnitDied(int unitId, string name)
        {
            UnitId = unitId;
            Name = name;
        }
    }

    /// <summary>战斗终局。</summary>
    public readonly struct BattleEnded
    {
        public readonly bool PlayerWon;
        public BattleEnded(bool playerWon) => PlayerWon = playerWon;
    }
}
