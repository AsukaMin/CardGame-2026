using System.Collections.Generic;

namespace Battle
{
    /// <summary>命令结果：UI→核心下命令，核心返回可读的失败原因（UI 框架文档契约："要返回值、要失败原因"）。</summary>
    public class PlayResult
    {
        public bool Success;
        public FailReason Reason;
        public string Error;

        public static PlayResult Ok() => new PlayResult { Success = true };

        public static PlayResult Fail(FailReason reason, string error) =>
            new PlayResult { Success = false, Reason = reason, Error = error };
    }

    /// <summary>战斗终局：供 run 层接管（剩余行动点带入下一场、部位卡回收清单）。</summary>
    public class BattleResult
    {
        public bool PlayerWon;
        public int Turns;
        /// <summary>战斗结束时剩余的全局行动点（run 内继续携带）。</summary>
        public int RemainingActionPoints;
        /// <summary>战后可回收的部位卡配置 Id（未召唤+存活单位的全部部位，加阵亡包中非消耗部位）。</summary>
        public List<int> RecoverablePartCardIds = new List<int>();
        /// <summary>随单位死亡销毁的消耗型部位卡配置 Id。</summary>
        public List<int> DestroyedPartCardIds = new List<int>();
        /// <summary>存活到终局的玩家单位 Id。</summary>
        public List<int> SurvivorUnitIds = new List<int>();
    }

    /// <summary>只读快照：订阅事件后全量刷新界面用（UI 框架文档"订阅后立刻全量刷"）。</summary>
    public class BattleSnapshot
    {
        public BattlePhase Phase;
        public int Turn;
        public bool Finished;
        public bool PlayerWon;
        public int PlayerHp, PlayerMaxHp, EnemyHp, EnemyMaxHp;
        public int ActionPoints;
        public int DeckCount, HandCount, GraveyardPackageCount;
        public List<UnitView> Units = new List<UnitView>();
        public List<HandCardView> Hand = new List<HandCardView>();
        public List<RosterView> Roster = new List<RosterView>();
        public List<string> EnemyIntents = new List<string>();

        public class UnitView
        {
            public int Id;
            public Side Owner;
            public string Name;
            public int Hp, MaxHp, Attack;
            public bool IsTaunt, AttackedThisTurn;
        }

        public class HandCardView
        {
            public int CardInstanceId, ConfigId;
            public string Name;
            public int[] TierCosts;
            public string[] TierLabels;
        }

        public class RosterView
        {
            public int RosterIndex, ConfigId;
            public string Name;
            public int SummonCost, Attack, MaxHp;
            public RosterStatus Status;
        }
    }
}
