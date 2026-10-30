using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>自动对局结果。</summary>
    public readonly struct AutoBattleResult
    {
        public readonly BattleResult Result;
        public readonly int Turns;
        public AutoBattleResult(BattleResult result, int turns) { Result = result; Turns = turns; }
    }

    /// <summary>
    /// 自动对局（M1 完成标准的驱动器）：简易玩家策略打到终局——
    /// 能召唤就召唤 → 效果卡打敌方主战者 → 我方怪兽全部攻击 → 结束回合。
    /// 测试与编辑器冒烟共用；同种子同操作=同终局（确定性）。
    /// </summary>
    public static class AutoBattle
    {
        /// <summary>回合上限（数据失衡/策略死循环时快速失败，不带病运行）。</summary>
        public const int MaxTurns = 200;

        public static AutoBattleResult Run(BattleSetup setup)
        {
            BattleSession session = new BattleSession(setup);
            session.Start();
            while (!session.IsBattleOver)
            {
                if (session.Turn > MaxTurns)
                    throw new InvalidOperationException($"超过 {MaxTurns} 回合未分胜负（数据失衡或策略死循环）");
                PlayPlayerTurn(session);
            }
            return new AutoBattleResult(session.Result, session.Turn);
        }

        /// <summary>玩家回合的简易策略（编辑器冒烟与测试共用）。</summary>
        public static void PlayPlayerTurn(BattleSession session)
        {
            // 1. 能召唤就召唤（能量花在怪兽上）
            bool acted = true;
            while (acted && !session.IsBattleOver)
            {
                acted = false;
                for (int i = 0; i < session.Hand.Count; i++)
                {
                    BattleCard card = session.Hand[i];
                    if (card.IsMonster && card.Cost <= session.Energy)
                    {
                        session.SummonMonster(i);
                        acted = true;
                        break;
                    }
                }
            }

            // 2. 效果卡全部打向敌方主战者
            acted = true;
            while (acted && !session.IsBattleOver)
            {
                acted = false;
                for (int i = 0; i < session.Hand.Count; i++)
                {
                    BattleCard card = session.Hand[i];
                    if (!card.IsMonster && card.Cost <= session.Energy)
                    {
                        session.PlayEffectCard(i, session.EnemyLeader.Id);
                        acted = true;
                        break;
                    }
                }
            }

            // 3. 我方怪兽全部攻击敌方主战者
            List<SkillEntity> board = new List<SkillEntity>(session.PlayerBoard);
            for (int i = 0; i < board.Count && !session.IsBattleOver; i++)
                session.Attack(board[i].Id, session.EnemyLeader.Id);

            // 4. 结束回合（本回合攻击已致终局则无需再结束）
            if (!session.IsBattleOver)
                session.EndTurn();
        }
    }

    /// <summary>
    /// 对局编排（M5·T3 数据化版）：牌组与敌方阵容定义（GameData/decks.json / enemies.json）→ BattleSetup。
    /// 卡定义、组牌、敌方阵容三层全部数据化；牌库顺序 = 条目序展开（count 连续重复）——抽牌顺序敏感，数据保序。
    /// 牌组 1（演示牌组）与敌人 3002（演武场守卫）的展开序与退役的 M1 硬编码版逐值等价
    /// （同种子零漂移锚定依赖它——《BattleFlowTests.M1HardcodedSetup》对照仍有效）。
    /// </summary>
    public static class DemoBattleData
    {
        public static BattleSetup CreateSetup(CardCatalog catalog, DeckDef deck, EnemyDef enemy, int seed = 0)
        {
            BattleSetup setup = new BattleSetup
            {
                PlayerHp = 30,
                EnemyHp = enemy.LeaderHp,
                InitialEnergy = 2,
                RandomSeed = seed,
                Cards = catalog,
            };
            foreach (DeckEntry entry in deck.Cards)
                for (int i = 0; i < entry.Count; i++)
                    setup.Deck.Add(catalog.Get(entry.Id));
            foreach (EnemySummonEntry entry in enemy.Summons)
                for (int i = 0; i < entry.Count; i++)
                    setup.EnemySummons.Add(catalog.Get(entry.Id).Monster);
            return setup;
        }
    }
}
