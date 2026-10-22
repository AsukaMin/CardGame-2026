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
    /// 演示对局的组牌编排（M2 数据管线版）：按 id 从 CardCatalog 取卡组 deck。
    /// 卡定义数值在 GameData/cards.json（数据侧）；组牌编排（id×张数）与敌方序列留代码侧——数据化归 M5 内容管线。
    /// </summary>
    public static class DemoBattleData
    {
        /// <summary>默认演示对局（30 对 30，12 张混合牌库，敌方依序召唤 小鬼×2 → 食人魔）。与 M1 硬编码版逐值等价。</summary>
        public static BattleSetup CreateSetup(CardCatalog catalog, int seed = 0)
        {
            BattleSetup setup = new BattleSetup
            {
                PlayerHp = 30,
                EnemyHp = 30,
                InitialEnergy = 2,
                RandomSeed = seed,
            };
            setup.EnemySummons.AddRange(new[]
            {
                catalog.Get(2003).Monster, catalog.Get(2003).Monster, catalog.Get(2004).Monster,
            }); // 小鬼×2 → 食人魔
            setup.Deck.AddRange(new[]
            {
                catalog.Get(2001), catalog.Get(1002), catalog.Get(2002),
                catalog.Get(1001), catalog.Get(2001), catalog.Get(1002),
                catalog.Get(1001), catalog.Get(2002), catalog.Get(2001),
                catalog.Get(1001), catalog.Get(1002), catalog.Get(2002),
            }); // 幼狼×3 电火花×3 石像×3 火球×3（顺序与 M1 一致——同种子回归锚定依赖它）
            return setup;
        }
    }
}
