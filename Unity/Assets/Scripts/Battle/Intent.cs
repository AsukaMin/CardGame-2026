namespace Battle
{
    /// <summary>敌方意图基类：敌方回合开始前揭示，玩家先看谜面再应对（B+意图）。</summary>
    public abstract class Intent
    {
        public abstract string Describe();
        public override string ToString() => Describe();
    }

    /// <summary>攻击玩家 X 点。</summary>
    public sealed class AttackPlayerIntent : Intent
    {
        public int Damage;
        public override string Describe() => $"攻击玩家 {Damage} 点";
    }

    /// <summary>攻击玩家的某个单位 X 点（生成时预选目标，玩家可见打谁）。</summary>
    public sealed class AttackUnitIntent : Intent
    {
        public int TargetUnitId;
        public int Damage;
        public override string Describe() => $"攻击我方单位 #{TargetUnitId} {Damage} 点";
    }

    /// <summary>召唤敌方卡套池中的一个预整备卡套。</summary>
    public sealed class SummonIntent : Intent
    {
        public int PoolIndex;
        public override string Describe() => "召唤";
    }

    /// <summary>施法：执行一组效果（目标相对敌方阵营）。</summary>
    public sealed class CastIntent : Intent
    {
        public EffectDef[] Effects;
        public override string Describe() => "施法";
    }
}
