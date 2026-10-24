using System;
using System.Collections.Generic;
using System.IO;
using Battle;
using GameEvents;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>
    /// T4 数据驱动验证卡用例：真 GameData/cards.json → CardCatalog → BattleSession 全链。
    /// 打击（配置伤害闭环）/ 防御（护盾先吸收，Rounds=2 扛过敌方回合）/ 抽牌（宿主 op 进 JSON）/
    /// 处刑（Damage→Conditional 击杀奖励——同步扣血、事件延迟派发的时序契约锁定）。
    /// </summary>
    [TestFixture]
    public class DataDrivenCardTests
    {
        static CardCatalog catalog;

        [SetUp]
        public void ClearBus() => GameEventBus.ClearAll();

        [OneTimeSetUp]
        public void LoadCatalog() =>
            catalog = CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));

        [Test]
        public void 打击_打出造成配置伤害()
        {
            BattleSession session = NewSession(new[] { 1003 });
            session.Start();

            Assert.That(session.Hand[0].Name, Is.EqualTo("打击"));
            int before = session.EnemyHp;
            session.PlayEffectCard(0, session.EnemyLeader.Id);

            Assert.That(session.EnemyHp, Is.EqualTo(before - 6), "伤害 = cards.json 配置值 6");
        }

        [Test]
        public void 防御_开盾扛过敌方回合_下回合开始回落()
        {
            BattleSession session = NewSession(new[] { 1004 }, Monster("突袭者", 3, 3));
            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id);

            Assert.That(session.PlayerLeader.Attributes.Current(AttributeId.Shield), Is.EqualTo(5), "Self 修饰 +5 盾");

            session.EndTurn(); // 敌方回合同步跑完：召唤突袭者并攻击

            Assert.That(session.PlayerHp, Is.EqualTo(30), "3 攻被 5 盾全吸收，血不动");
            Assert.That(session.PlayerLeader.Attributes.Current(AttributeId.Shield), Is.EqualTo(0),
                "Rounds=2：扛过敌方回合，下个我方回合开始（TickRound 1→0）回落");
        }

        [Test]
        public void 防御_伤害超过盾则穿透扣血()
        {
            BattleSession session = NewSession(new[] { 1004 }, Monster("重锤", 8, 8));
            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id);

            session.EndTurn();

            Assert.That(session.PlayerHp, Is.EqualTo(30 - 3), "8 攻先耗 5 盾，穿透 3");
        }

        [Test]
        public void 抽牌_打出抽两张()
        {
            BattleSession session = NewSession(new[] { 1005, 1003, 1003, 1003, 1003, 1003 });
            session.Start();

            Assert.That(session.Hand.Count, Is.EqualTo(4), "起手 3 + 首回合抽 1");
            Assert.That(session.DeckCount, Is.EqualTo(2));
            session.PlayEffectCard(0, session.EnemyLeader.Id); // hand[0] = 牌库顶 = 抽牌卡

            Assert.That(session.Hand.Count, Is.EqualTo(5), "打出 -1、抽牌 +2");
            Assert.That(session.DeckCount, Is.EqualTo(0));
            Assert.That(session.Hand[3].Name, Is.EqualTo("打击"));
            Assert.That(session.Hand[4].Name, Is.EqualTo("打击"));
        }

        [Test]
        public void 抽牌_牌库不足_抽到多少算多少不崩()
        {
            BattleSession session = NewSession(new[] { 1005, 1003 });
            session.Start();

            Assert.That(session.DeckCount, Is.EqualTo(0), "起手已抽空牌库");
            session.PlayEffectCard(0, session.EnemyLeader.Id);

            Assert.That(session.Hand.Count, Is.EqualTo(1), "打出 1 张、无牌可抽");
        }

        [Test]
        public void 处刑_击杀触发抽牌_事件顺序正确()
        {
            BattleSession session = NewSession(new[] { 1006, 1003, 1003, 1003, 1003, 1003 }, Monster("炮灰", 4, 4));
            session.Start();
            session.EndTurn(); // 敌方召唤炮灰（4 攻 4 血）并攻击我方主战者；我方第 2 回合开始抽 1

            Assert.That(session.EnemyBoard.Count, Is.EqualTo(1));
            Assert.That(session.Hand.Count, Is.EqualTo(5), "起手 4 + 回合抽 1");
            List<string> order = new List<string>();
            GameEventBus.Subscribe<CardPlayed>(e => order.Add("played"));
            GameEventBus.Subscribe<CardDrawn>(e => order.Add("drawn"));
            session.PlayEffectCard(0, session.EnemyBoard[0].Id); // 处刑 6 伤打死 4 血炮灰

            Assert.That(session.EnemyBoard, Is.Empty, "击杀离场");
            Assert.That(session.Hand.Count, Is.EqualTo(5), "打出 -1、击杀奖励抽 +1");
            Assert.That(session.DeckCount, Is.EqualTo(0));
            CollectionAssert.AreEqual(new[] { "played", "drawn" }, order, "先出牌事实、后嵌套抽牌");
        }

        [Test]
        public void 处刑_未击杀不触发奖励()
        {
            BattleSession session = NewSession(new[] { 1006, 1003, 1003, 1003, 1003, 1003 });
            session.Start();

            session.PlayEffectCard(0, session.EnemyLeader.Id); // 6 伤打 30 血主战者，未击杀

            Assert.That(session.Hand.Count, Is.EqualTo(3), "只消耗出牌（起手 4 - 1），无奖励抽牌");
            Assert.That(session.DeckCount, Is.EqualTo(2), "牌库原封未动——Then 未触发");
        }

        [Test]
        public void 中毒_周期跳伤到期_标签随效授予与移除()
        {
            ApplyEffectOp apply = (ApplyEffectOp)catalog.Get(1007).Effect.Effects[0].Ops[0];
            Assert.That(apply.Effect.Rounds, Is.EqualTo(4), "真文件形状锁定：rounds=4");
            Assert.That(apply.Effect.GrantedTags, Is.EqualTo(new[] { "Keyword.Poison" }));
            Assert.That(apply.Effect.PeriodicEachRound, Is.True);

            BattleSession session = NewSession(new[] { 1007, 1003, 1003, 1003, 1003, 1003 });
            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 挂毒敌方主战者

            Assert.That(session.EnemyLeader.HasTag("Keyword.Poison"), Is.True, "grantedTags：中毒期间拥有标签");
            Assert.That(session.EnemyHp, Is.EqualTo(30), "挂上不跳伤");

            session.EndTurn(); // 敌方回合开始 tick1 + 我方回合开始 tick2：-4
            Assert.That(session.EnemyHp, Is.EqualTo(26), "每 tick 按配置 2 伤");
            Assert.That(session.EnemyLeader.HasTag("Keyword.Poison"), Is.True, "未到期标签仍在");

            session.EndTurn(); // tick3 + tick4 → 到期移除
            Assert.That(session.EnemyHp, Is.EqualTo(22), "四次共 8 伤");
            Assert.That(session.EnemyLeader.HasTag("Keyword.Poison"), Is.False, "到期标签随效摘除");

            session.EndTurn();
            Assert.That(session.EnemyHp, Is.EqualTo(22), "到期不再跳伤");
        }

        [Test]
        public void 中毒_跳伤致死怪兽_效果随实体清场()
        {
            BattleSession session = NewSession(new[] { 1007, 1003, 1003, 1003, 1003, 1003 }, Monster("毒囊兽", 0, 3));
            session.Start();
            session.EndTurn(); // 敌方召唤毒囊兽（0 攻 3 血）

            Assert.That(session.EnemyBoard.Count, Is.EqualTo(1));
            SkillEntity victim = session.EnemyBoard[0];
            session.PlayEffectCard(0, victim.Id); // 我方回合挂毒（2 伤/跳）
            Assert.That(victim.HasTag("Keyword.Poison"), Is.True);

            session.EndTurn(); // tick1（3→1，敌方回合开始）+ tick2（1→0，我方回合开始）→ 死亡清扫

            Assert.That(session.EnemyBoard, Is.Empty, "跳伤致死离场");
            Assert.That(victim.HasTag("Keyword.Poison"), Is.False, "效果随实体移除，授予标签一并摘除");
            Assert.That(session.PlayerHp, Is.EqualTo(30), "0 攻怪兽无伤（对照组）");
        }

        [Test]
        public void 抽牌op_JSON往返保真()
        {
            string json = SkillJson.Serialize(new DrawCardsOp { Count = 3 });

            StringAssert.Contains("\"op\": \"DrawCards\"", json);
            DrawCardsOp copy = SkillJson.Deserialize<DrawCardsOp>(json);
            Assert.That(copy.Count, Is.EqualTo(3));
        }

        // ==================== 构造辅助（BattleFlowTests 房风） ====================

        static BattleSession NewSession(int[] deckIds, MonsterCardDef summon = null)
        {
            BattleSetup setup = new BattleSetup();
            if (summon != null) setup.EnemySummons.Add(summon);
            foreach (int id in deckIds) setup.Deck.Add(catalog.Get(id));
            return new BattleSession(setup);
        }

        static MonsterCardDef Monster(string name, int attack, int maxHp) =>
            new MonsterCardDef { Name = name, Attack = attack, MaxHp = maxHp };
    }
}
