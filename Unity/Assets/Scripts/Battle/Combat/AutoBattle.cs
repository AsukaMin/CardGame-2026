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

    /// <summary>演示卡池与对局数据（M1 冒烟用；M2 数据管线接管后由表驱动取代）。</summary>
    public static class DemoBattleData
    {
        /// <summary>火球术：2 费，对指定目标造成 4 点伤害。</summary>
        public static EffectCardDef Fireball() => new EffectCardDef
        {
            Name = "火球术", Cost = 2,
            Effects = new[]
            {
                new EffectGroupDef
                {
                    Target = TargetMode.Chosen,
                    Ops = new EffectOp[]
                    {
                        new DamageOp { Amount = ScopedValue.Const(4), Kind = DamageSourceKind.Effect },
                    },
                },
            },
        };

        /// <summary>电火花：1 费，对指定目标造成 2 点伤害。</summary>
        public static EffectCardDef Spark() => new EffectCardDef
        {
            Name = "电火花", Cost = 1,
            Effects = new[]
            {
                new EffectGroupDef
                {
                    Target = TargetMode.Chosen,
                    Ops = new EffectOp[]
                    {
                        new DamageOp { Amount = ScopedValue.Const(2), Kind = DamageSourceKind.Effect },
                    },
                },
            },
        };

        /// <summary>幼狼：2 费 2/2。</summary>
        public static MonsterCardDef Wolf() =>
            new MonsterCardDef { Name = "幼狼", Cost = 2, Attack = 2, MaxHp = 2 };

        /// <summary>石像：3 费 3/4。</summary>
        public static MonsterCardDef Golem() =>
            new MonsterCardDef { Name = "石像", Cost = 3, Attack = 3, MaxHp = 4 };

        /// <summary>小鬼（敌方）：2/3。</summary>
        public static MonsterCardDef Imp() =>
            new MonsterCardDef { Name = "小鬼", Cost = 0, Attack = 2, MaxHp = 3 };

        /// <summary>食人魔（敌方）：4/5。</summary>
        public static MonsterCardDef Ogre() =>
            new MonsterCardDef { Name = "食人魔", Cost = 0, Attack = 4, MaxHp = 5 };

        /// <summary>默认演示对局（30 对 30，玩家 12 张混合牌库，敌方依序召唤 小鬼×2 → 食人魔）。</summary>
        public static BattleSetup CreateSetup(int seed = 0)
        {
            BattleSetup setup = new BattleSetup
            {
                PlayerHp = 30,
                EnemyHp = 30,
                InitialEnergy = 2,
                RandomSeed = seed,
                EnemySummons = { Imp(), Imp(), Ogre() },
            };
            setup.Deck.AddRange(new BattleCard[]
            {
                BattleCard.Of(Wolf()), BattleCard.Of(Spark()), BattleCard.Of(Golem()),
                BattleCard.Of(Fireball()), BattleCard.Of(Wolf()), BattleCard.Of(Spark()),
                BattleCard.Of(Fireball()), BattleCard.Of(Golem()), BattleCard.Of(Wolf()),
                BattleCard.Of(Fireball()), BattleCard.Of(Spark()), BattleCard.Of(Golem()),
            });
            return setup;
        }
    }
}
