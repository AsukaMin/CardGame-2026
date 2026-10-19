using System;

namespace Battle
{
    /// <summary>回合阶段（M1 流转：我方出牌 ⇄ 敌方回合 → 终局）。</summary>
    public enum BattlePhase
    {
        NotStarted = 0,
        /// <summary>我方回合——唯一子阶段"出牌阶段"与回合开始重合（M1 无更多子阶段）。</summary>
        PlayerPlay,
        /// <summary>敌方回合——召唤→全员攻击，同步执行完才交还。</summary>
        EnemyTurn,
        GameOver,
    }

    /// <summary>
    /// 专用回合阶段机（M1 定案：不占用 StateTree——状态树定位为单位级行为，留给未来敌人 AI）。
    /// 命令驱动、同步推进：每个公共方法返回前完成该阶段全部结算与事实广播。
    /// 流转：Start → 我方回合 → EndPlayerTurn → 敌方回合(召唤→攻击) → 我方回合 → … → GameOver。
    /// </summary>
    public sealed class TurnFlow
    {
        readonly BattleSession session;

        public TurnFlow(BattleSession session) => this.session = session;

        public BattlePhase Phase { get; private set; } = BattlePhase.NotStarted;

        /// <summary>回合数（进入我方回合时 +1；我方回合 N 与其后的敌方回合 N 同数）。</summary>
        public int Turn { get; private set; }

        /// <summary>战斗开始：进入第 1 个我方回合。</summary>
        public void Start()
        {
            if (Phase != BattlePhase.NotStarted)
                throw new InvalidOperationException("战斗已开始，不可重复 Start");
            session.OnBattleStarted();
            EnterPlayerTurn();
        }

        /// <summary>玩家结束回合（出牌阶段的唯一出口）→ 敌方回合 → 交还我方回合或终局。</summary>
        public void EndPlayerTurn()
        {
            RequirePlayerPlay();
            session.OnSideTurnEnded(isPlayer: true);
            EnterEnemyTurn();
        }

        void EnterPlayerTurn()
        {
            Turn++;
            Phase = BattlePhase.PlayerPlay;
            session.OnSideTurnStarted(isPlayer: true);
            if (session.IsBattleOver) Phase = BattlePhase.GameOver; // 时点效果致死（未来），收口
        }

        void EnterEnemyTurn()
        {
            Phase = BattlePhase.EnemyTurn;
            session.OnSideTurnStarted(isPlayer: false);
            if (!session.IsBattleOver)
            {
                session.RunEnemySummon();       // 召唤阶段：依序一只（耗尽跳过）
                if (!session.IsBattleOver)
                    session.RunEnemyAttacks();  // 攻击阶段：场上怪兽各一次（任一方死亡即中断）
            }
            session.OnSideTurnEnded(isPlayer: false);
            if (session.IsBattleOver) { Phase = BattlePhase.GameOver; return; }
            EnterPlayerTurn();
        }

        /// <summary>推入终局（胜负判定归 session，阶段流转归本机）。</summary>
        public void MarkGameOver() => Phase = BattlePhase.GameOver;

        /// <summary>命令前置校验：仅出牌阶段接受玩家命令。</summary>
        internal void RequirePlayerPlay()
        {
            if (Phase != BattlePhase.PlayerPlay)
                throw new InvalidOperationException($"当前阶段 {Phase} 不接受该命令（仅出牌阶段可行动）");
        }
    }
}
