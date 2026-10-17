using System.Collections.Generic;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>整场流程用例：胜负两路径、事件契约、种子确定性。</summary>
    [TestFixture]
    public class BattleFlowTests
    {
        List<string> eventLog;

        [SetUp]
        public void SetUp()
        {
            eventLog = new List<string>();
            BattleEvents.OnCardDrawn += OnDraw;
            BattleEvents.OnCardPlayed += OnPlayed;
            BattleEvents.OnIntentRevealed += OnIntent;
            BattleEvents.OnTurnStart += OnTurn;
            BattleEvents.OnBattleEnd += OnEnd;
        }

        [TearDown]
        public void TearDown()
        {
            BattleEvents.OnCardDrawn -= OnDraw;
            BattleEvents.OnCardPlayed -= OnPlayed;
            BattleEvents.OnIntentRevealed -= OnIntent;
            BattleEvents.OnTurnStart -= OnTurn;
            BattleEvents.OnBattleEnd -= OnEnd;
        }

        void OnDraw(int id) => eventLog.Add("Draw");
        void OnPlayed(int id) => eventLog.Add("Play");
        void OnIntent(System.Collections.Generic.IReadOnlyList<Intent> intents) => eventLog.Add("Intent");
        void OnTurn(int turn) => eventLog.Add($"Turn{turn}");
        void OnEnd(BattleResult result) => eventLog.Add("End");

        /// <summary>可获胜配置：12 张火球、30 起始点数、玩家 50 血、敌方 20 血，固定抽牌顺序。</summary>
        static BattleSetup WinnableSetup()
        {
            var deck = new List<EffectCardConfig>();
            for (int i = 0; i < 12; i++) deck.Add(SampleData.Fireball());
            var order = new List<int>();
            for (int i = 0; i < 12; i++) order.Add(301);
            return new BattleSetup
            {
                Tuning = new BattleTuning { InitialActionPoints = 30, PlayerMaxHp = 50 },
                DeckCards = deck,
                FixedDeckOrder = order,
                PlayerRoster =
                {
                    SampleData.Build(SampleData.FighterSleeve(), SampleData.Claw(), SampleData.VenomGland(), SampleData.Grudge()),
                },
                Enemy = SampleData.BasicEnemy(),
                Seed = 42,
            };
        }

        /// <summary>剧本：每回合把手牌火球尽量打出去（优先完全体），然后结束回合。</summary>
        static void ScriptedAggro(BattleSession battle)
        {
            int guard = 0;
            while (!battle.Finished && guard++ < 40)
            {
                BattleSnapshot snap = battle.GetSnapshot();
                for (int i = snap.Hand.Count - 1; i >= 0; i--)
                    for (int t = snap.Hand[i].TierCosts.Length - 1; t >= 0; t--)
                        if (battle.PlayCard(i, t, BattleSession.EnemyHeroId).Success) break;
                if (!battle.Finished) battle.EndTurn();
            }
        }

        [Test]
        public void 完整战斗_击杀敌方主战者获胜()
        {
            BattleSession battle = new BattleSession(WinnableSetup());
            ScriptedAggro(battle);

            Assert.IsTrue(battle.Finished);
            Assert.IsTrue(battle.Result.PlayerWon);
            Assert.GreaterOrEqual(battle.Result.RemainingActionPoints, 0);
            Assert.AreEqual(0, battle.EnemyHp);
            Assert.AreEqual(1, eventLog.FindAll(e => e == "End").Count); // 终局事件恰好一次
        }

        [Test]
        public void 完整战斗_玩家死亡判负()
        {
            var setup = new BattleSetup
            {
                Tuning = new BattleTuning { InitialActionPoints = 0, PlayerMaxHp = 10, StartingHandSize = 0, CardsDrawnPerTurn = 0 },
                DeckCards = new List<EffectCardConfig>(),
                PlayerRoster = new List<MonsterSleeve>(),
                Enemy = SampleData.BasicEnemy(), // 无召唤池无施法 → 意图必为攻击玩家
                Seed = 9,
            };
            BattleSession battle = new BattleSession(setup);

            int guard = 0;
            while (!battle.Finished && guard++ < 20) battle.EndTurn();

            Assert.IsTrue(battle.Finished);
            Assert.IsFalse(battle.Result.PlayerWon);
            Assert.AreEqual(0, battle.PlayerHp);
        }

        [Test]
        public void 事件契约_开局顺序为抽牌意图回合()
        {
            // 抽起手 → 揭示首回合意图 → 广播回合开始（UI 依赖此顺序做首帧刷新）
            new BattleSession(WinnableSetup());
            CollectionAssert.AreEqual(
                new[] { "Draw", "Draw", "Draw", "Draw", "Intent", "Turn1" },
                eventLog);
        }

        [Test]
        public void 同种子同操作_终局一致()
        {
            BattleResult a = ScriptedRun(777);
            BattleResult b = ScriptedRun(777);
            Assert.AreEqual(a.PlayerWon, b.PlayerWon);
            Assert.AreEqual(a.Turns, b.Turns);
            Assert.AreEqual(a.RemainingActionPoints, b.RemainingActionPoints);
            Assert.AreEqual(a.RecoverablePartCardIds.Count, b.RecoverablePartCardIds.Count);
        }

        static BattleResult ScriptedRun(int seed)
        {
            BattleSetup setup = WinnableSetup();
            setup.Seed = seed;
            setup.Tuning.InitialActionPoints = 12;
            setup.Recovery = new SampleData.ScriptedRecoveryPolicy { PointsPerTurnStart = 5 };
            BattleSession battle = new BattleSession(setup);
            ScriptedAggro(battle);
            return battle.Result;
        }
    }
}
