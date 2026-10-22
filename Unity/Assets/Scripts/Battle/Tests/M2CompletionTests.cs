using System;
using System.IO;
using Battle;
using GameEvents;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>
    /// M2 完成标准证明锚（任务清单 T6）：① 加载 cards.json 断言验证卡形状与数值；
    /// ② 未硬编码直接证明——同一 CardCatalog.Load 读一份只改数值的 fixture，断言行为跟着变；
    /// ③ 数据卡驱动对局的行为主链（打击伤害/防御吸收/抽牌张数/处刑条件）由 DataDrivenCardTests 8 用例覆盖；
    /// ④ 同种子确定性由 BattleFlowTests 完整对局节（含 M1 零漂移对照）保持。
    /// </summary>
    [TestFixture]
    public class M2CompletionTests
    {
        /// <summary>与真表同结构、只改数值的对照表：打击 9 伤 / 防御 7 盾 / 抽牌 3 张 / 处刑 8 伤。</summary>
        const string RetunedFixture =
            "[" +
            "{\"kind\":\"effect\",\"id\":1003,\"name\":\"打击\",\"cost\":1,\"effects\":[" +
            "{\"target\":\"Chosen\",\"ops\":[{\"op\":\"Damage\",\"amount\":{\"kind\":\"Const\",\"value\":9}}]}]}," +
            "{\"kind\":\"effect\",\"id\":1004,\"name\":\"防御\",\"cost\":1,\"effects\":[" +
            "{\"target\":\"Self\",\"ops\":[{\"op\":\"ModifyAttribute\",\"attribute\":\"Shield\",\"mod\":\"Add\"," +
            "\"magnitude\":{\"kind\":\"Const\",\"value\":7},\"duration\":\"Rounds\",\"rounds\":2}]}]}," +
            "{\"kind\":\"effect\",\"id\":1005,\"name\":\"抽牌\",\"cost\":1,\"effects\":[" +
            "{\"target\":\"Self\",\"ops\":[{\"op\":\"DrawCards\",\"count\":3}]}]}," +
            "{\"kind\":\"effect\",\"id\":1006,\"name\":\"处刑\",\"cost\":2,\"effects\":[" +
            "{\"target\":\"Chosen\",\"ops\":[" +
            "{\"op\":\"Damage\",\"amount\":{\"kind\":\"Const\",\"value\":8}}," +
            "{\"op\":\"Conditional\",\"condition\":{\"kind\":\"AttributeCompare\",\"attribute\":\"CurrentHp\"," +
            "\"op\":\"LessOrEqual\",\"value\":0},\"then\":[{\"op\":\"DrawCards\",\"count\":1}]}]}]}" +
            "]";

        [SetUp]
        public void ClearBus() => GameEventBus.ClearAll();

        static CardCatalog LoadRealTable() =>
            CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));

        [Test]
        public void 形状数值_四张验证卡与卡表定义一致()
        {
            CardCatalog catalog = LoadRealTable();

            // 打击：1 费，Chosen，Damage 6
            EffectCardDef strike = catalog.Get(1003).Effect;
            Assert.That(strike.Name, Is.EqualTo("打击"));
            Assert.That(strike.Cost, Is.EqualTo(1));
            Assert.That(strike.Effects[0].Target, Is.EqualTo(TargetMode.Chosen));
            Assert.That(((DamageOp)strike.Effects[0].Ops[0]).Amount.Value, Is.EqualTo(6));

            // 防御：1 费，Self，Shield +5（Rounds=2）
            EffectCardDef defense = catalog.Get(1004).Effect;
            Assert.That(defense.Cost, Is.EqualTo(1));
            Assert.That(defense.Effects[0].Target, Is.EqualTo(TargetMode.Self));
            ModifyAttributeOp shield = (ModifyAttributeOp)defense.Effects[0].Ops[0];
            Assert.That(shield.Attribute, Is.EqualTo(AttributeId.Shield));
            Assert.That(shield.Op, Is.EqualTo(ModOp.Add));
            Assert.That(shield.Magnitude.Value, Is.EqualTo(5));
            Assert.That(shield.Duration, Is.EqualTo(EffectDurationKind.Rounds));
            Assert.That(shield.Rounds, Is.EqualTo(2));

            // 抽牌：1 费，DrawCards 2
            EffectCardDef draw = catalog.Get(1005).Effect;
            Assert.That(draw.Cost, Is.EqualTo(1));
            Assert.That(((DrawCardsOp)draw.Effects[0].Ops[0]).Count, Is.EqualTo(2));

            // 处刑：2 费，Damage 6 + 击杀条件（CurrentHp≤0）→ 抽 1
            EffectCardDef execute = catalog.Get(1006).Effect;
            Assert.That(execute.Cost, Is.EqualTo(2));
            Assert.That(((DamageOp)execute.Effects[0].Ops[0]).Amount.Value, Is.EqualTo(6));
            ConditionalOp reward = (ConditionalOp)execute.Effects[0].Ops[1];
            AttributeCompareCondition kill = (AttributeCompareCondition)reward.Condition;
            Assert.That(kill.Attribute, Is.EqualTo(AttributeId.CurrentHp));
            Assert.That(kill.Op, Is.EqualTo(CompareOperator.LessOrEqual));
            Assert.That(((DrawCardsOp)reward.Then[0]).Count, Is.EqualTo(1));
        }

        [Test]
        public void 未硬编码证明_同一加载器读改数值fixture_行为跟着变()
        {
            CardCatalog catalog = CardCatalog.Load(RetunedFixture); // 与真表同一条加载代码路径

            // 打击 9：伤害跟随数据（真表为 6）
            BattleSession strike = NewSession(catalog, new[] { 1003 });
            strike.Start();
            int before = strike.EnemyHp;
            strike.PlayEffectCard(0, strike.EnemyLeader.Id);
            Assert.That(strike.EnemyHp, Is.EqualTo(before - 9), "伤害 = fixture 配置值 9");

            // 防御 7 盾：扛住 6 攻（真表 5 盾会穿透 1 掉血）
            BattleSession defense = NewSession(catalog, new[] { 1004 }, Monster("重击", 6, 6));
            defense.Start();
            defense.PlayEffectCard(0, defense.EnemyLeader.Id);
            Assert.That(defense.PlayerLeader.Attributes.Current(AttributeId.Shield), Is.EqualTo(7), "盾 = fixture 配置值 7");
            defense.EndTurn();
            Assert.That(defense.PlayerHp, Is.EqualTo(30), "7 盾全吸收 6 攻");

            // 抽牌 3：手牌增量跟随数据（真表为 +2）
            BattleSession draw = NewSession(catalog, new[] { 1005, 1003, 1003, 1003, 1003, 1003, 1003, 1003 });
            draw.Start(); // 起手 3 + 回合抽 1 = 手 4、牌库 4
            draw.PlayEffectCard(0, draw.EnemyLeader.Id);
            Assert.That(draw.Hand.Count, Is.EqualTo(3 + 3), "打出 -1、抽 +3");
            Assert.That(draw.DeckCount, Is.EqualTo(1));

            // 处刑 8：伤害跟随数据（真表为 6）
            BattleSession execute = NewSession(catalog, new[] { 1006 });
            execute.Start();
            int hpBefore = execute.EnemyHp;
            execute.PlayEffectCard(0, execute.EnemyLeader.Id);
            Assert.That(execute.EnemyHp, Is.EqualTo(hpBefore - 8), "伤害 = fixture 配置值 8");
        }

        // ==================== 构造辅助 ====================

        static BattleSession NewSession(CardCatalog catalog, int[] deckIds, MonsterCardDef summon = null)
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
