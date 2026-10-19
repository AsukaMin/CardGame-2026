using System;
using System.Collections.Generic;
using Battle;
using GameEvents;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>
    /// M1 战斗核心用例：回合流转 / 能量经济 / 出牌结算 / 召唤攻击约束 / 胜负 / 完整自动对局（完成标准）。
    /// 事实事件经 GameEventBus 订阅断言（用例前后 ClearAll 防串扰——事件系统设计§4）。
    /// </summary>
    [TestFixture]
    public class BattleFlowTests
    {
        [SetUp]
        public void SetUp() => GameEventBus.ClearAll();

        [TearDown]
        public void TearDown() => GameEventBus.ClearAll();

        // ---------- 便捷构造 ----------

        static EffectCardDef DmgCard(string name, int cost, int damage) => new EffectCardDef
        {
            Name = name, Cost = cost,
            Effects = new[]
            {
                new EffectGroupDef
                {
                    Target = TargetMode.Chosen,
                    Ops = new EffectOp[]
                    {
                        new DamageOp { Amount = ScopedValue.Const(damage), Kind = DamageSourceKind.Effect },
                    },
                },
            },
        };

        static MonsterCardDef Monster(string name, int cost, int atk, int hp) =>
            new MonsterCardDef { Name = name, Cost = cost, Attack = atk, MaxHp = hp };

        static BattleSetup Setup(int playerHp = 30, int enemyHp = 30,
            MonsterCardDef[] summons = null, params BattleCard[] deck) =>
            new BattleSetup
            {
                PlayerHp = playerHp,
                EnemyHp = enemyHp,
                EnemySummons = summons != null ? new List<MonsterCardDef>(summons) : new List<MonsterCardDef>(),
                Deck = new List<BattleCard>(deck),
            };

        static List<T> Record<T>(Func<T, bool> filter = null) where T : struct
        {
            List<T> log = new List<T>();
            void Handler(T e) { if (filter == null || filter(e)) log.Add(e); }
            GameEventBus.Subscribe<T>(Handler);
            return log;
        }

        // ==================== 回合流转 ====================

        [Test]
        public void 回合流转_开局进入我方出牌阶段()
        {
            List<BattleStarted> started = Record<BattleStarted>();
            List<TurnStarted> turns = Record<TurnStarted>();
            List<CardDrawn> draws = Record<CardDrawn>();

            BattleSession session = new BattleSession(Setup(deck: new[] { BattleCard.Of(DmgCard("火", 1, 1)) }));
            session.Start();

            Assert.AreEqual(BattlePhase.PlayerPlay, session.Phase, "开局即我方出牌阶段");
            Assert.AreEqual(1, session.Turn);
            Assert.AreEqual(1, started.Count, "BattleStarted 广播一次");
            Assert.AreEqual(1, turns.Count, "TurnStarted 广播一次");
            Assert.IsTrue(turns[0].IsPlayerSide);
            Assert.AreEqual(1, draws.Count, "起手抽 1 张（牌库只有 1 张）；回合开始再抽时牌库已空，跳过");
            Assert.AreEqual(1, session.Hand.Count);

            Assert.AreEqual(4, session.Energy, "初始 2 + 每回合回复 2");
        }

        [Test]
        public void 回合流转_结束回合后敌方行动并回到我方回合()
        {
            List<MonsterSummoned> summons = Record<MonsterSummoned>();
            List<TurnStarted> turns = Record<TurnStarted>();

            BattleSession session = new BattleSession(Setup(
                summons: new[] { Monster("小鬼", 0, 2, 3) }, deck: new[] { BattleCard.Of(DmgCard("火", 1, 1)) }));
            session.Start();
            session.EndTurn();

            Assert.AreEqual(BattlePhase.PlayerPlay, session.Phase, "敌方回合在 EndTurn 返回前同步执行完");
            Assert.AreEqual(2, session.Turn, "回到第 2 个我方回合");
            Assert.AreEqual(1, session.EnemyBoard.Count, "敌方召唤了一只");
            Assert.AreEqual(1, summons.FindAll(s => !s.IsPlayerSide).Count);
            Assert.AreEqual(3, turns.Count, "我方→敌方→我方，各广播一次回合开始");
            Assert.IsTrue(turns[2].IsPlayerSide, "最后一次是第 2 个我方回合");
            Assert.AreEqual(28, session.PlayerHp, "小鬼攻击了一次玩家（无我方怪兽，目标唯一）");
        }

        [Test]
        public void 回合流转_未开始或终局后命令被拒()
        {
            BattleSession session = new BattleSession(Setup(enemyHp: 1, deck: new[] { BattleCard.Of(DmgCard("火", 1, 5)) }));
            Assert.Throws<InvalidOperationException>(() => session.PlayEffectCard(0, 1), "未开始不可出牌");

            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 敌方 1 血，直接击杀
            Assert.AreEqual(BattlePhase.GameOver, session.Phase);
            Assert.Throws<InvalidOperationException>(() => session.EndTurn(), "终局后不可结束回合");
            Assert.Throws<InvalidOperationException>(() => session.PlayEffectCard(0, session.EnemyLeader.Id), "终局后不可出牌");
        }

        // ==================== 能量经济 ====================

        [Test]
        public void 能量经济_不足拒绝出牌且状态不变()
        {
            BattleSession session = new BattleSession(Setup(deck: new[] { BattleCard.Of(DmgCard("大火", 9, 1)) }));
            session.Start();

            Assert.Throws<InvalidOperationException>(() =>
                session.PlayEffectCard(0, session.EnemyLeader.Id));
            Assert.AreEqual(4, session.Energy, "扣费失败能量不变");
            Assert.AreEqual(1, session.Hand.Count, "手牌不变（牌库仅 1 张已入起手）");
            Assert.AreEqual(30, session.EnemyHp, "未结算");
        }

        [Test]
        public void 能量经济_回复策略按钩子时点生效()
        {
            List<string> calls = new List<string>();
            BattleSetup setup = Setup(deck: new[] { BattleCard.Of(Monster("狼", 2, 2, 2)) });
            setup.EnergyRule = new CallbackRule(ctx => calls.Add(ctx.IsPlayerSide ? "我方回合" : "敌方回合"),
                () => calls.Add("攻击后"));
            BattleSession session = new BattleSession(setup);
            session.Start();                       // 我方回合 1
            session.SummonMonster(0);              // 出牌阶段召唤
            session.Attack(session.PlayerBoard[0].Id, session.EnemyLeader.Id);

            CollectionAssert.AreEqual(
                new[] { "我方回合", "攻击后" }, calls, "回合开始钩子先于攻击后钩子");
        }

        sealed class CallbackRule : IEnergyRule
        {
            readonly Action<EnergyContext> onTurn;
            readonly Action onAttack;
            public CallbackRule(Action<EnergyContext> onTurn, Action onAttack)
            { this.onTurn = onTurn; this.onAttack = onAttack; }
            public void OnTurnStarted(EnergyContext ctx) => onTurn(ctx);
            public void OnPlayerAttackDone(EnergyContext ctx) => onAttack();
            public void OnCardDiscarded(EnergyContext ctx) { }
        }

        // ==================== 出牌结算 ====================

        [Test]
        public void 出牌结算_伤害走技能管线并按序广播事实()
        {
            List<CardPlayed> played = Record<CardPlayed>();
            List<DamageResolved> damages = Record<DamageResolved>();
            List<EnergyChanged> energyChanges = Record<EnergyChanged>();

            BattleSession session = new BattleSession(Setup(deck: new[] { BattleCard.Of(DmgCard("火球", 2, 4)) }));
            session.Start();
            int energyBefore = session.Energy;
            session.PlayEffectCard(0, session.EnemyLeader.Id);

            Assert.AreEqual(26, session.EnemyHp, "30-4");
            Assert.AreEqual(1, played.Count);
            Assert.AreEqual("火球", played[0].CardName);
            Assert.AreEqual(1, damages.Count, "效果伤害广播一次");
            Assert.AreEqual(4, damages[0].Amount);
            Assert.AreEqual(26, damages[0].TargetHpAfter);
            Assert.AreEqual(energyBefore - 2, session.Energy, "扣面值费用");
            Assert.IsTrue(energyChanges.Count >= 1, "扣费经 EnergyChanged 广播");
        }

        [Test]
        public void 出牌结算_效果卡可指定敌方怪兽()
        {
            List<UnitDied> deaths = Record<UnitDied>();
            BattleSession session = new BattleSession(Setup(
                summons: new[] { Monster("小鬼", 0, 2, 3) }, deck: new[] { BattleCard.Of(DmgCard("火球", 2, 4)) }));
            session.Start();
            session.EndTurn(); // 敌方召唤小鬼并攻击玩家

            Assert.AreEqual(30, session.EnemyHp, "敌方主战者未掉血");
            SkillEntity imp = session.EnemyBoard[0];
            session.PlayEffectCard(0, imp.Id);
            Assert.AreEqual(0, session.EnemyBoard.Count, "4 伤击杀 3 血小鬼，已离场");
            Assert.AreEqual(1, deaths.Count, "死亡广播");
        }

        [Test]
        public void 出牌结算_目标必须敌方侧()
        {
            BattleSession session = new BattleSession(Setup(deck: new[]
            {
                BattleCard.Of(Monster("狼", 2, 2, 2)), BattleCard.Of(DmgCard("火球", 2, 4)),
            }));
            session.Start();
            session.SummonMonster(0);

            Assert.Throws<InvalidOperationException>(() =>
                session.PlayEffectCard(0, session.PlayerBoard[0].Id), "不可打己方单位");
            Assert.Throws<InvalidOperationException>(() =>
                session.PlayEffectCard(0, 9999), "目标不存在抛异常");
        }

        // ==================== 召唤与攻击 ====================

        [Test]
        public void 召唤_属性正确并广播()
        {
            List<MonsterSummoned> summons = Record<MonsterSummoned>();
            BattleSession session = new BattleSession(Setup(deck: new[] { BattleCard.Of(Monster("狼", 2, 2, 2)) }));
            session.Start();
            session.SummonMonster(0);

            Assert.AreEqual(1, session.PlayerBoard.Count);
            Assert.AreEqual(2, session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.Attack));
            Assert.AreEqual(2, session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.MaxHp));
            Assert.AreEqual(0, session.Hand.Count, "召唤消耗手牌");
            Assert.AreEqual(1, summons.Count);
            Assert.IsTrue(summons[0].IsPlayerSide);
        }

        [Test]
        public void 攻击_一回合一次_战斗伤害走管线()
        {
            List<AttackResolved> attacks = Record<AttackResolved>();
            BattleSession session = new BattleSession(Setup(deck: new[] { BattleCard.Of(Monster("狼", 2, 2, 2)) }));
            session.Start();
            SkillEntity wolf = null;
            session.SummonMonster(0);
            wolf = session.PlayerBoard[0];

            session.Attack(wolf.Id, session.EnemyLeader.Id);
            Assert.AreEqual(28, session.EnemyHp, "30-2（战斗伤害）");
            Assert.AreEqual(1, attacks.Count);
            Assert.AreEqual(2, attacks[0].Amount);

            Assert.Throws<InvalidOperationException>(() =>
                session.Attack(wolf.Id, session.EnemyLeader.Id), "同回合第二次攻击被拒");
        }

        [Test]
        public void 攻击_召唤当回合可攻击_击杀敌方怪兽离场()
        {
            List<UnitDied> deaths = Record<UnitDied>();
            BattleSession session = new BattleSession(Setup(
                summons: new[] { Monster("小鬼", 0, 2, 3) }, deck: new[] { BattleCard.Of(Monster("石像", 3, 3, 4)) }));
            session.Start();
            session.SummonMonster(0);
            session.EndTurn(); // 敌方召唤小鬼

            SkillEntity golem = session.PlayerBoard[0];
            session.Attack(golem.Id, session.EnemyBoard[0].Id);

            Assert.AreEqual(0, session.EnemyBoard.Count, "3 攻击杀 3 血小鬼");
            Assert.AreEqual(1, deaths.Count, "死亡广播");
            Assert.AreEqual("小鬼", deaths[0].Name);
        }

        [Test]
        public void 攻击_敌方怪兽每回合各攻击一次()
        {
            BattleSession session = new BattleSession(Setup(
                summons: new[] { Monster("小鬼", 0, 2, 3), Monster("小鬼", 0, 2, 3) },
                deck: new[] { BattleCard.Of(Monster("狼", 2, 2, 2)) }));
            List<AttackResolved> attacks = Record<AttackResolved>();
            session.Start();
            session.SummonMonster(0);
            session.EndTurn(); // 敌方回合 1：召唤第 1 只并攻击

            Assert.AreEqual(1, session.EnemyBoard.Count);
            Assert.AreEqual(1, attacks.Count, "敌方回合 1 攻击一次");

            session.EndTurn(); // 敌方回合 2：召唤第 2 只，两只各攻击一次
            Assert.AreEqual(2, session.EnemyBoard.Count);
            Assert.AreEqual(3, attacks.Count, "敌方回合 2 两只各一次（累计 3）——目标随机但攻击数固定");
        }

        // ==================== 胜负 ====================

        [Test]
        public void 胜负_击杀敌方主战者判胜并终局()
        {
            List<BattleEnded> ended = Record<BattleEnded>();
            BattleSession session = new BattleSession(Setup(enemyHp: 4, deck: new[] { BattleCard.Of(DmgCard("火球", 2, 4)) }));
            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id);

            Assert.AreEqual(BattleResult.Victory, session.Result);
            Assert.AreEqual(BattlePhase.GameOver, session.Phase);
            Assert.AreEqual(1, ended.Count);
            Assert.IsTrue(ended[0].PlayerWon);
        }

        [Test]
        public void 胜负_我方主战者归零判负()
        {
            List<BattleEnded> ended = Record<BattleEnded>();
            BattleSession session = new BattleSession(Setup(
                playerHp: 3, summons: new[] { Monster("食人魔", 0, 5, 5) }));
            session.Start();
            session.EndTurn(); // 敌方召唤食人魔并攻击（玩家无场怪，目标唯一）

            Assert.AreEqual(BattleResult.Defeat, session.Result);
            Assert.AreEqual(BattlePhase.GameOver, session.Phase);
            Assert.AreEqual(1, ended.Count);
            Assert.IsFalse(ended[0].PlayerWon);
        }

        // ==================== 完整对局（✅ 完成标准） ====================

        [Test]
        public void 完整对局_自动跑完分出胜负()
        {
            AutoBattleResult result = AutoBattle.Run(DemoBattleData.CreateSetup(seed: 123));
            Assert.AreNotEqual(BattleResult.None, result.Result, "必须分出胜负");
            Assert.Greater(result.Turns, 0);
            Assert.LessOrEqual(result.Turns, AutoBattle.MaxTurns);
        }

        [Test]
        public void 完整对局_同种子重跑结果一致()
        {
            AutoBattleResult a = AutoBattle.Run(DemoBattleData.CreateSetup(seed: 42));
            AutoBattleResult b = AutoBattle.Run(DemoBattleData.CreateSetup(seed: 42));
            Assert.AreEqual(a.Result, b.Result, "同种子同操作=同终局（确定性）");
            Assert.AreEqual(a.Turns, b.Turns);
        }

        [Test]
        public void 完整对局_不同种子均能终局()
        {
            int[] seeds = { 1, 2, 3, 4, 5, 20261019 /* 编辑器冒烟固定种子 */ };
            foreach (int seed in seeds)
            {
                AutoBattleResult result = AutoBattle.Run(DemoBattleData.CreateSetup(seed: seed));
                Assert.AreNotEqual(BattleResult.None, result.Result, $"种子 {seed} 必须分出胜负");
            }
        }
    }
}
