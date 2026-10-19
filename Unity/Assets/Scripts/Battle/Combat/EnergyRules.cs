namespace Battle
{
    /// <summary>
    /// 能量回复规则（策划案 v2.1【框架先行】：回复手段开局选择——三钩子+策略接口，具体清单后补）。
    /// 宿主在钩子时点回调；回复经 ctx.Session.GainEnergy 落地（自动广播事实事件）。
    /// </summary>
    public interface IEnergyRule
    {
        /// <summary>回合开始（双方回合都回调；策略按 IsPlayerSide 自行决定是否回复）。</summary>
        void OnTurnStarted(EnergyContext ctx);

        /// <summary>我方怪兽攻击结算完毕后。</summary>
        void OnPlayerAttackDone(EnergyContext ctx);

        /// <summary>弃牌后（M1 无弃牌命令——钩子留位，暂不触发）。</summary>
        void OnCardDiscarded(EnergyContext ctx);
    }

    /// <summary>回复钩子上下文。</summary>
    public sealed class EnergyContext
    {
        public BattleSession Session;
        public int Turn;
        public bool IsPlayerSide;
    }

    /// <summary>默认策略：每我方回合开始固定 +N。</summary>
    public sealed class FlatPerTurnRule : IEnergyRule
    {
        readonly int amount;

        public FlatPerTurnRule(int amount) => this.amount = amount;

        public void OnTurnStarted(EnergyContext ctx)
        {
            if (ctx.IsPlayerSide) ctx.Session.GainEnergy(amount);
        }

        public void OnPlayerAttackDone(EnergyContext ctx) { }
        public void OnCardDiscarded(EnergyContext ctx) { }
    }
}
