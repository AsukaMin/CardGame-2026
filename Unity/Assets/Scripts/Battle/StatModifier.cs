namespace Battle
{
    /// <summary>
    /// 一条属性/关键词修饰，分两个桶存放：
    /// 临时桶（效果直接施加，按时长过期）与光环桶（常驻槽部位产生，单位离场即整桶失效、由战场统一重算）。
    /// </summary>
    public class StatModifier
    {
        public StatType Stat;
        public int Amount;
        public bool IsKeywordMod;
        public Keywords Keyword;
        public ModifierDurationKind Kind = ModifierDurationKind.Battle;
        /// <summary>Kind==Turns 时的剩余轮数（每个玩家回合开始递减）。</summary>
        public int RemainingTurns = 1;
        /// <summary>光环来源单位（追踪与调试用）。</summary>
        public BattleUnit AuraSource;
    }
}
