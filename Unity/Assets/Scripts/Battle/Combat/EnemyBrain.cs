using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 敌方占位 AI（最小规则："初期测试就简单写为从预设列表召唤一只，然后场上所有怪兽进行一次攻击"）。
    /// 攻击目标 = 种子随机（玩家主战者或我方在场怪兽等权）——让怪兽血量参与意义；
    /// M6 意图系统接管时替换本类即可，回合编排（TurnFlow）不动。
    /// </summary>
    public sealed class EnemyBrain
    {
        /// <summary>召唤阶段：依序取下一只预设怪兽（耗尽跳过）。敌方召唤免费（M1 敌方无能量经济）。</summary>
        public void RunSummon(BattleSession session)
        {
            MonsterCardDef next = session.NextEnemySummon();
            if (next != null) session.SummonEnemyMonster(next);
        }

        /// <summary>攻击阶段：场上敌方怪兽各攻击一次（快照遍历；任一方主战者死亡即中断）。</summary>
        public void RunAttacks(BattleSession session)
        {
            List<SkillEntity> attackers = session.SnapshotEnemyBoard();
            for (int i = 0; i < attackers.Count; i++)
            {
                if (session.IsBattleOver) return;
                SkillEntity target = PickTarget(session);
                if (target == null) return;
                session.PerformEnemyAttack(attackers[i], target);
            }
        }

        /// <summary>目标选择占位：玩家主战者与我方在场怪兽等权随机。</summary>
        SkillEntity PickTarget(BattleSession session)
        {
            List<SkillEntity> candidates = new List<SkillEntity> { session.PlayerLeader };
            candidates.AddRange(session.PlayerBoard);
            return session.Random.Pick((IReadOnlyList<SkillEntity>)candidates);
        }
    }
}
