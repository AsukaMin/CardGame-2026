namespace Battle
{
    /// <summary>全局行动点：跨回合不清零（run 内携带），增长完全依赖回复策略。</summary>
    public class ActionEconomy
    {
        int points;

        public int Points => points;

        public ActionEconomy(int initial) => points = initial;

        public bool TrySpend(int amount)
        {
            if (amount < 0 || points < amount) return false;
            points -= amount;
            BattleEvents.FireActionPointsChanged(points);
            return true;
        }

        public void Grant(int amount)
        {
            if (amount <= 0) return;
            points += amount;
            BattleEvents.FireActionPointsChanged(points);
        }
    }

    /// <summary>
    /// 行动点回复策略（v2.1：框架先行，具体手段后补）。
    /// 战斗在三个钩子点位回调，策略通过 ctx.Economy.Grant 发点。
    /// 候选实现方向：攻击回复 / 弃牌回复 / 每回合投骰（数值走配置，随机必须走核心注入的 Rng——框架已留好接入位）。
    /// </summary>
    public interface IRecoveryPolicy
    {
        /// <summary>玩家回合开始（含第一回合）。</summary>
        void OnTurnStart(RecoveryContext ctx);

        /// <summary>我方单位完成一次攻击后。</summary>
        void OnFriendlyAttackCompleted(RecoveryContext ctx);

        /// <summary>主动弃牌后。</summary>
        void OnCardDiscarded(RecoveryContext ctx);
    }

    /// <summary>回复钩子上下文。</summary>
    public class RecoveryContext
    {
        public Side Side = Side.Player;
        public int TurnNumber;
        public int AttackerUnitId;
        public int DiscardedCardInstanceId;
        public ActionEconomy Economy;
    }
}
