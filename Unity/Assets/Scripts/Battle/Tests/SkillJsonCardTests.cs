using System;
using Battle;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>
    /// T2 卡牌 JSON 形状用例：扁平信封（kind/id/name/cost + 卡体）读写 / 单卡加载期校验抛错矩阵 /
    /// 六张 M1 演示卡全量往返（形状对 cards.json 现存卡种的全表达覆盖证明）。
    /// </summary>
    [TestFixture]
    public class SkillJsonCardTests
    {
        // ==================== 往返 ====================

        [Test]
        public void 往返_怪兽卡_全字段保真()
        {
            CardEntry wolf = Card(2001, "幼狼", 2, 2, 2);

            string json = RoundTrip(wolf, out CardEntry copy);

            StringAssert.Contains("\"kind\": \"monster\"", json);
            StringAssert.Contains("\"id\": 2001", json);
            StringAssert.Contains("\"attack\": 2", json);
            StringAssert.Contains("\"maxHp\": 2", json);
            Assert.That(copy.Card.IsMonster, Is.True);
            Assert.That(copy.Card.Monster.Name, Is.EqualTo("幼狼"));
            Assert.That(copy.Card.Monster.MaxHp, Is.EqualTo(2));
        }

        [Test]
        public void 往返_效果卡_效果载荷走T1管线()
        {
            CardEntry fireball = Fireball();

            string json = RoundTrip(fireball, out CardEntry copy);

            StringAssert.Contains("\"kind\": \"effect\"", json);
            StringAssert.Contains("\"op\": \"Damage\"", json);
            Assert.That(copy.Card.IsMonster, Is.False);
            Assert.That(copy.Card.Effect.Name, Is.EqualTo("火球术"));
            DamageOp damage = (DamageOp)copy.Card.Effect.Effects[0].Ops[0];
            Assert.That(damage.Amount.Value, Is.EqualTo(4));
            Assert.That(damage.Kind, Is.EqualTo(DamageSourceKind.Effect));
        }

        [Test]
        public void 往返_M1六张演示卡_全表达覆盖()
        {
            CardEntry[] cards = { Fireball(), Spark(), Wolf(), Golem(), Imp(), Ogre() };

            RoundTrip(cards, out CardEntry[] copy);

            Assert.That(copy.Length, Is.EqualTo(6));
            // 关键值逐张抽查（与 cards.json 的 M1 移植卡原值一致）
            Assert.That(copy[0].Id, Is.EqualTo(1001));
            Assert.That(((DamageOp)copy[0].Card.Effect.Effects[0].Ops[0]).Amount.Value, Is.EqualTo(4));
            Assert.That(((DamageOp)copy[1].Card.Effect.Effects[0].Ops[0]).Amount.Value, Is.EqualTo(2));
            Assert.That(copy[1].Card.Effect.Cost, Is.EqualTo(1));
            Assert.That(copy[2].Card.Monster.Attack, Is.EqualTo(2));
            Assert.That(copy[3].Card.Monster.MaxHp, Is.EqualTo(4));
            Assert.That(copy[4].Card.Monster.Cost, Is.EqualTo(0)); // 敌方卡 0 费合法
            Assert.That(copy[5].Card.Monster.Attack, Is.EqualTo(4));
        }

        [Test]
        public void 手写最小JSON_怪兽卡省略字段取默认()
        {
            CardEntry card = SkillJson.Deserialize<CardEntry>(
                "{ \"kind\": \"monster\", \"id\": 2005, \"name\": \"盾墙\", \"maxHp\": 5 }");
            Assert.That(card.Card.Monster.Cost, Is.EqualTo(0));    // 字段默认值即合理默认
            Assert.That(card.Card.Monster.Attack, Is.EqualTo(0));  // 0 攻怪兽（墙）合法
        }

        // ==================== 抛错矩阵（单卡加载期校验） ====================

        [Test]
        public void 抛错_未知卡牌kind() =>
            AssertThrowsContaining("未知卡牌判别值",
                () => SkillJson.Deserialize<CardEntry>("{ \"kind\": \"hero\", \"id\": 1, \"name\": \"x\", \"maxHp\": 1 }"));

        [Test]
        public void 抛错_缺失卡牌kind() =>
            AssertThrowsContaining("缺少判别字段",
                () => SkillJson.Deserialize<CardEntry>("{ \"id\": 2001, \"name\": \"幼狼\", \"maxHp\": 2 }"));

        [Test]
        public void 抛错_id缺失或非正() =>
            AssertThrowsContaining("id 必须为正整数",
                () => SkillJson.Deserialize<CardEntry>("{ \"kind\": \"monster\", \"id\": 0, \"name\": \"幼狼\", \"maxHp\": 2 }"));

        [Test]
        public void 抛错_名字为空() =>
            AssertThrowsContaining("name 为空",
                () => SkillJson.Deserialize<CardEntry>("{ \"kind\": \"monster\", \"id\": 2001, \"maxHp\": 2 }"));

        [Test]
        public void 抛错_怪兽maxHp非正() =>
            AssertThrowsContaining("maxHp 必须为正",
                () => SkillJson.Deserialize<CardEntry>("{ \"kind\": \"monster\", \"id\": 2001, \"name\": \"幼狼\", \"maxHp\": 0 }"));

        [Test]
        public void 抛错_效果卡无效果载荷() =>
            AssertThrowsContaining("无效果载荷",
                () => SkillJson.Deserialize<CardEntry>("{ \"kind\": \"effect\", \"id\": 1003, \"name\": \"白板\" }"));

        [Test]
        public void 抛错_卡载荷深处坏标签_报错带数据路径() =>
            AssertThrowsContaining("effects[0].ops[0].tag", () => SkillJson.Deserialize<CardEntry>(
                "{ \"kind\": \"effect\", \"id\": 1003, \"name\": \"坏卡\", " +
                "\"effects\": [ { \"ops\": [ { \"op\": \"GrantTag\", \"tag\": \"Nope.X\" } ] } ] }"));

        // ==================== M1 演示卡构造（值对应 cards.json 1001/1002/2001-2004 的移植原值） ====================

        static CardEntry Fireball() => new CardEntry
        {
            Id = 1001,
            Card = BattleCard.Of(new EffectCardDef
            {
                Name = "火球术", Cost = 2,
                Effects = new[]
                {
                    new EffectGroupDef
                    {
                        Target = TargetMode.Chosen,
                        Ops = new EffectOp[] { new DamageOp { Amount = ScopedValue.Const(4) } },
                    },
                },
            }),
        };

        static CardEntry Spark() => new CardEntry
        {
            Id = 1002,
            Card = BattleCard.Of(new EffectCardDef
            {
                Name = "电火花", Cost = 1,
                Effects = new[]
                {
                    new EffectGroupDef
                    {
                        Target = TargetMode.Chosen,
                        Ops = new EffectOp[] { new DamageOp { Amount = ScopedValue.Const(2) } },
                    },
                },
            }),
        };

        static CardEntry Wolf() => Card(2001, "幼狼", 2, 2, 2);

        static CardEntry Golem() => Card(2002, "石像", 3, 3, 4);

        static CardEntry Imp() => Card(2003, "小鬼", 0, 2, 3);

        static CardEntry Ogre() => Card(2004, "食人魔", 0, 4, 5);

        static CardEntry Card(int id, string name, int cost, int attack, int maxHp) => new CardEntry
        {
            Id = id,
            Card = BattleCard.Of(new MonsterCardDef { Name = name, Cost = cost, Attack = attack, MaxHp = maxHp }),
        };

        // ==================== 辅助 ====================

        /// <summary>文本对照法断言往返稳定（同 SkillJsonTests，不给卡定义加 Equals）。</summary>
        static string RoundTrip<T>(T value, out T copy)
        {
            string json1 = SkillJson.Serialize(value);
            copy = SkillJson.Deserialize<T>(json1);
            string json2 = SkillJson.Serialize(copy);
            Assert.That(json2, Is.EqualTo(json1), "往返不稳定：同数据进出应一致");
            return json1;
        }

        /// <summary>转换器抛出的异常可能被 Newtonsoft 包一层，沿异常链深度查找报错片段。</summary>
        static void AssertThrowsContaining(string fragment, TestDelegate code)
        {
            Exception ex = Assert.Catch(code);
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e.Message.Contains(fragment)) return;
            Assert.Fail($"报错应包含 \"{fragment}\"，实际：{ex}");
        }
    }
}
