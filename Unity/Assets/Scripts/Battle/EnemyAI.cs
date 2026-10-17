using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 最笨可跑的 AI（文档：先做最笨的能跑——意图系统是 AI 质量的万能补丁）。
    /// 只做决策不做状态变更：生成意图 + 挑选单位攻击目标，执行交给 BattleSession。
    /// </summary>
    public class EnemyAI
    {
        readonly EnemyConfig config;
        readonly Rng rng;

        public EnemyAI(EnemyConfig config, Rng rng)
        {
            this.config = config;
            this.rng = rng;
        }

        /// <summary>生成下个敌方回合的意图（简单优先级 + 随机）。</summary>
        public Intent GenerateIntent(Battlefield field, IReadOnlyList<bool> summonPoolAvailable)
        {
            int damage = rng.Next(config.AttackDamageMin, config.AttackDamageMax + 1);
            List<BattleUnit> playerUnits = field.UnitsOf(Side.Player);

            // 玩家无单位：只会打脸
            if (playerUnits.Count == 0)
                return new AttackPlayerIntent { Damage = damage };

            int canSummon = 0;
            for (int i = 0; i < summonPoolAvailable.Count; i++)
                if (summonPoolAvailable[i]) canSummon++;
            bool canCast = config.CastPool.Length > 0;

            int roll = rng.Next(100);
            if (roll < 40)
                return new AttackPlayerIntent { Damage = damage };
            if (roll < 70)
                return new AttackUnitIntent { TargetUnitId = rng.Pick(playerUnits).Id, Damage = damage };
            if (roll < 90 && canSummon > 0)
            {
                for (int i = 0; i < summonPoolAvailable.Count; i++)
                    if (summonPoolAvailable[i])
                        return new SummonIntent { PoolIndex = i };
            }
            if (canCast)
                return new CastIntent { Effects = rng.Pick(config.CastPool) };
            return new AttackPlayerIntent { Damage = damage };
        }

        /// <summary>敌方单位攻击目标：尊重玩家嘲讽；否则从玩家单位与玩家本人中等概率随机。</summary>
        public EffectTarget PickUnitAttackTarget(Battlefield field)
        {
            List<BattleUnit> playerUnits = field.UnitsOf(Side.Player);
            List<BattleUnit> taunts = new List<BattleUnit>();
            foreach (BattleUnit u in playerUnits)
                if (u.HasKeyword(Keywords.Taunt)) taunts.Add(u);
            if (taunts.Count > 0)
                return new EffectTarget(rng.Pick(taunts));

            List<EffectTarget> options = new List<EffectTarget>();
            foreach (BattleUnit u in playerUnits)
                options.Add(new EffectTarget(u));
            options.Add(new EffectTarget(Side.Player));
            return rng.Pick(options);
        }
    }
}
