using System;
using System.IO;
using Battle;
using GameEvents;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>
    /// M5·T1 三条 API 缺口清偿的验证用例（内联 fixture JSON，不动真 cards.json——T4 量产时再入表）。
    /// T1a 触发器卡（打出即注册，SelfOnly 反弹 / SourceOnly+归因）/ T1b LastDamageKilled 击杀联动 /
    /// T1c AddCardToHandOp 复制卡 + 卡表二遍巡检（卡 id 引用存在性）。
    /// </summary>
    [TestFixture]
    public class M5GapTests
    {
        [SetUp]
        public void ClearBus() => GameEventBus.ClearAll();

        // ==================== fixture 卡（纯触发卡 / 归因触发卡 / 击杀奖励 / 复制卡） ====================

        const string Thorns =
            "{\"kind\":\"effect\",\"id\":9001,\"name\":\"荆棘结界\",\"cost\":1," +
            "\"triggers\":[{\"eventTag\":\"Event.Damage.Applied\",\"selfOnly\":true," +
            "\"effects\":{\"target\":\"Instigator\",\"ops\":[{\"op\":\"Damage\",\"amount\":{\"kind\":\"Const\",\"value\":2.0},\"kind\":\"Effect\"}]}}]}";

        const string Leech =
            "{\"kind\":\"effect\",\"id\":9003,\"name\":\"汲血印记\",\"cost\":1," +
            "\"triggers\":[{\"eventTag\":\"Event.Damage.Applied\",\"sourceOnly\":true," +
            "\"filter\":{\"kind\":\"EventSourceCard\",\"cardId\":9004}," +
            "\"effects\":{\"target\":\"Self\",\"ops\":[{\"op\":\"DrawCards\",\"count\":1}]}}]}";

        const string Fireball =
            "{\"kind\":\"effect\",\"id\":9004,\"name\":\"火弹\",\"cost\":1," +
            "\"effects\":[{\"target\":\"Chosen\",\"ops\":[{\"op\":\"Damage\",\"amount\":{\"kind\":\"Const\",\"value\":2.0},\"kind\":\"Effect\"}]}]}";

        const string Zap =
            "{\"kind\":\"effect\",\"id\":9005,\"name\":\"电光\",\"cost\":1," +
            "\"effects\":[{\"target\":\"Chosen\",\"ops\":[{\"op\":\"Damage\",\"amount\":{\"kind\":\"Const\",\"value\":1.0},\"kind\":\"Effect\"}]}]}";

        const string ExecBonus =
            "{\"kind\":\"effect\",\"id\":9010,\"name\":\"处决奖励\",\"cost\":2," +
            "\"effects\":[{\"target\":\"Chosen\",\"ops\":[{\"op\":\"Damage\",\"amount\":{\"kind\":\"Const\",\"value\":6.0},\"kind\":\"Effect\"}]}," +
            "{\"target\":\"Self\",\"ops\":[{\"op\":\"Conditional\",\"condition\":{\"kind\":\"LastDamageKilled\"}," +
            "\"then\":[{\"op\":\"Heal\",\"amount\":{\"kind\":\"Const\",\"value\":3.0}}],\"else\":[]}]}]}";

        const string Scholar =
            "{\"kind\":\"effect\",\"id\":9020,\"name\":\"学者\",\"cost\":1," +
            "\"effects\":[{\"target\":\"Self\",\"ops\":[{\"op\":\"AddCardToHand\",\"cardId\":9020,\"count\":1}]}]}";

        const string BadCopyRef =
            "{\"kind\":\"effect\",\"id\":9021,\"name\":\"坏复制\",\"cost\":1," +
            "\"effects\":[{\"target\":\"Self\",\"ops\":[{\"op\":\"AddCardToHand\",\"cardId\":9999,\"count\":1}]}]}";

        const string BadFilterRef =
            "{\"kind\":\"effect\",\"id\":9022,\"name\":\"坏归因\",\"cost\":1," +
            "\"triggers\":[{\"eventTag\":\"Event.Damage.Applied\",\"sourceOnly\":true," +
            "\"filter\":{\"kind\":\"EventSourceCard\",\"cardId\":8888}," +
            "\"effects\":{\"target\":\"Self\",\"ops\":[{\"op\":\"DrawCards\",\"count\":1}]}}]}";

        // ==================== T1a 触发器卡 ====================

        [Test]
        public void T1a_触发器卡_己方受伤反弹攻击者()
        {
            BattleSession session = NewSession(FixtureCatalog(Thorns), new[] { 9001 },
                Monster("突袭者", 3, 5));
            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 打出荆棘（纯触发卡：effects 空、载荷全在 triggers）

            session.EndTurn(); // 敌方回合：召唤突袭者并攻击玩家领袖

            Assert.That(session.PlayerHp, Is.EqualTo(30 - 3), "领袖被突袭者打 3");
            Assert.That(session.EnemyBoard.Count, Is.EqualTo(1));
            Assert.That(session.EnemyBoard[0].Attributes.CurrentInt(AttributeId.CurrentHp),
                Is.EqualTo(5 - 2), "SelfOnly 反弹：2 伤害回给攻击者（Target=Instigator 词汇首用）");
        }

        [Test]
        public void T1a_归因_SourceOnly与来源卡判定()
        {
            BattleSession session = NewSession(FixtureCatalog(Leech, Fireball, Zap),
                new[] { 9003, 9004, 9005, 9005, 9005, 9005 });
            session.Start(); // 起手 3 + 首回合抽 1：手 [汲血印记, 火弹, 电光, 电光]，牌库 2

            session.PlayEffectCard(0, session.EnemyLeader.Id); // 汲血印记：注册归因触发器
            int enemyHpBefore = session.EnemyHp;
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 火弹（9004）：归因命中 → 触发抽 1

            Assert.That(session.DeckCount, Is.EqualTo(1), "火弹伤害归因 9004 命中 → 抽 1（牌库 2→1）");
            Assert.That(session.EnemyHp, Is.EqualTo(enemyHpBefore - 2));

            session.EndTurn(); // 敌方无召唤无攻击；我方回合 +2 能量
            int deckBefore = session.DeckCount;
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 电光（9005）：归因不匹配 → 不触发

            Assert.That(session.DeckCount, Is.EqualTo(deckBefore), "伤害卡 id≠9004 → EventSourceCard 判定不过，不抽牌");
        }

        [Test]
        public void T1a_序列化_触发器卡往返稳定()
        {
            CardEntry[] src =
            {
                new CardEntry
                {
                    Id = 9003,
                    Card = BattleCard.Of(new EffectCardDef
                    {
                        Name = "汲血印记", Cost = 1,
                        Triggers = new[]
                        {
                            new TriggerDef
                            {
                                EventTag = "Event.Damage.Applied", SourceOnly = true,
                                Filter = new EventSourceCardCondition { CardId = 9004 },
                                Effects = new EffectGroupDef
                                {
                                    Target = TargetMode.Self,
                                    Ops = new EffectOp[] { new DrawCardsOp { Count = 1 } },
                                },
                            },
                        },
                    }),
                },
            };
            string json = SkillJson.Serialize(src);
            CardEntry[] back = SkillJson.Deserialize<CardEntry[]>(json);

            Assert.That(SkillJson.Serialize(back), Is.EqualTo(json), "POCO→JSON→POCO→JSON 字符串一致（往返稳定合同）");
            TriggerDef trigger = back[0].Card.Effect.Triggers[0];
            Assert.That(trigger.EventTag, Is.EqualTo("Event.Damage.Applied"));
            Assert.That(trigger.SourceOnly, Is.True);
            Assert.That(trigger.Filter, Is.InstanceOf<EventSourceCardCondition>());
            Assert.That(((EventSourceCardCondition)trigger.Filter).CardId, Is.EqualTo(9004));
            Assert.That(back[0].Card.Id, Is.EqualTo(9003), "信封 id 注入卡身份（BattleCard.Id）");
        }

        [Test]
        public void T1a_序列化_触发器坏标签报错带数据路径() =>
            Assert.That(() => SkillJson.Deserialize<CardEntry>(
                    "{\"kind\": \"effect\", \"id\": 9001, \"name\": \"坏卡\"," +
                    "\"effects\": [{ \"target\": \"Self\", \"ops\": [{ \"op\": \"DrawCards\", \"count\": 1 }] }]," +
                    "\"triggers\": [{ \"eventTag\": \"Nope.X\", \"effects\": { \"target\": \"Self\", \"ops\": [] } }]}"),
                Throws.InvalidOperationException.With.Message.Contains("triggers[0].eventTag"));

        // ==================== T1b 击杀联动（条件记忆词汇） ====================

        [Test]
        public void T1b_击杀奖励_击杀后回血自己()
        {
            BattleSession session = NewSession(FixtureCatalog(ExecBonus), new[] { 9010 },
                Monster("炮灰", 5, 5));
            session.Start();
            session.EndTurn(); // 敌方回合：炮灰攻击领袖 30→25

            Assert.That(session.PlayerHp, Is.EqualTo(25));
            session.PlayEffectCard(0, session.EnemyBoard[0].Id); // 处决奖励：6 伤致死 → LastDamageKilled → 自己回血 3

            Assert.That(session.EnemyBoard.Count, Is.EqualTo(0), "炮灰被击杀");
            Assert.That(session.PlayerHp, Is.EqualTo(25 + 3), "击杀→自己回血（Target=Self，不再奶到刚被打死的目标）");
        }

        [Test]
        public void T1b_击杀奖励_未击杀不回血()
        {
            BattleSession session = NewSession(FixtureCatalog(ExecBonus), new[] { 9010 },
                Monster("石壳", 5, 10));
            session.Start();
            session.EndTurn(); // 领袖 30→25

            session.PlayEffectCard(0, session.EnemyBoard[0].Id); // 6 伤未致死（10→4）

            Assert.That(session.EnemyBoard[0].Attributes.CurrentInt(AttributeId.CurrentHp), Is.EqualTo(4));
            Assert.That(session.PlayerHp, Is.EqualTo(25), "未击杀 → LastDamageKilled 为假，不回血");
        }

        // ==================== T1c 复制卡 ====================

        [Test]
        public void T1c_复制卡_获得一张自身复制()
        {
            BattleSession session = NewSession(FixtureCatalog(Scholar), new[] { 9020, 9020 });
            session.Start(); // 起手 2 张学者

            session.PlayEffectCard(0, session.EnemyLeader.Id);

            Assert.That(session.Hand.Count, Is.EqualTo(2), "打出 -1、复制 +1");
            Assert.That(session.Hand[1].Name, Is.EqualTo("学者"));
            Assert.That(session.Hand[1].Id, Is.EqualTo(9020), "复制件携带信封卡 id（卡身份可用于归因）");

            session.PlayEffectCard(1, session.EnemyLeader.Id); // 复制件本身可再打出
            Assert.That(session.Hand.Count, Is.EqualTo(2), "复制件再打出又自复制——闭环成立");
        }

        [Test]
        public void T1c_卡表巡检_复制卡引用未定义卡号_加载即抛() =>
            Assert.That(() => CardCatalog.Load("[" + BadCopyRef + "]"),
                Throws.InvalidOperationException.With.Message.Contains("引用未定义的卡 id：9999"));

        [Test]
        public void T1c_卡表巡检_归因条件引用未定义卡号_加载即抛() =>
            Assert.That(() => CardCatalog.Load("[" + BadFilterRef + "," + Fireball + "]"),
                Throws.InvalidOperationException.With.Message.Contains("引用未定义的卡 id：8888"));

        // ==================== T2 挂账小项（盾值进事实事件 / attackedThisTurn 只读视图） ====================

        [Test]
        public void T2_盾值进事实事件_攻击被盾吸收()
        {
            CardCatalog cat = CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));
            BattleSession session = NewSession(cat, new[] { 1004 }, Monster("突袭者", 3, 3));
            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 防御卡：我方领袖 +5 盾

            AttackResolved? seen = null;
            GameEventBus.Subscribe<AttackResolved>(e => seen = e);
            session.EndTurn(); // 敌方回合：突袭者攻击领袖，3 攻被 5 盾全吸收

            Assert.That(seen, Is.Not.Null, "攻击结算广播了 AttackResolved");
            Assert.That(seen.Value.ShieldAbsorbed, Is.EqualTo(3), "吸收明细进事实字段");
            Assert.That(seen.Value.Amount, Is.EqualTo(0), "Amount 仍=实际扣血（M2 定案：事件值不含盾吸收）");
            Assert.That(seen.Value.TargetShieldAfter, Is.EqualTo(2));
        }

        [Test]
        public void T2_盾值进事实事件_无盾吸收为零()
        {
            CardCatalog cat = CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));
            BattleSession session = NewSession(cat, new[] { 1003 });
            session.Start();

            DamageResolved? seen = null;
            GameEventBus.Subscribe<DamageResolved>(e => seen = e);
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 打击：无盾直接扣血

            Assert.That(seen, Is.Not.Null);
            Assert.That(seen.Value.Amount, Is.EqualTo(6));
            Assert.That(seen.Value.ShieldAbsorbed, Is.EqualTo(0));
            Assert.That(seen.Value.TargetShieldAfter, Is.EqualTo(0));
        }

        [Test]
        public void T2_attackedThisTurn只读视图_攻击后置位_回合开始重置()
        {
            CardCatalog cat = CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));
            BattleSession session = NewSession(cat, new[] { 2001 });
            session.Start();
            session.SummonMonster(0);
            int unitId = session.PlayerBoard[0].Id;

            Assert.That(session.HasAttackedThisTurn(unitId), Is.False, "召唤当回合未攻击");

            session.Attack(unitId, session.EnemyLeader.Id);
            Assert.That(session.HasAttackedThisTurn(unitId), Is.True, "攻击后置位（UI 灰化依据）");

            session.EndTurn(); // 敌方无召唤无攻击；回到我方回合开始重置
            Assert.That(session.HasAttackedThisTurn(unitId), Is.False, "我方回合开始重置");
        }

        // ==================== T3 组牌与敌方阵容数据化 ====================

        [Test]
        public void T3_真文件加载_牌组保序_敌人三套齐全()
        {
            CardCatalog cards = CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));
            DeckCatalog decks = DeckCatalog.Load(File.ReadAllText(TestDataPaths.DecksJson()), cards);
            EnemyCatalog enemies = EnemyCatalog.Load(File.ReadAllText(TestDataPaths.EnemiesJson()), cards);

            Assert.That(decks.Count, Is.EqualTo(2));
            Assert.That(enemies.Count, Is.EqualTo(3), "三种敌人（快攻/均衡/Boss）");

            // 牌组 1 展开序 = 退役 M1 硬编码序（同种子零漂移锚定的数据侧保证）
            BattleSetup setup = DemoBattleData.CreateSetup(cards, decks.Get(1), enemies.Get(3002), seed: 0);
            string[] expected =
            {
                "幼狼", "电火花", "石像", "火球术", "幼狼", "电火花",
                "火球术", "石像", "幼狼", "火球术", "电火花", "石像",
            };
            Assert.That(setup.Deck.Count, Is.EqualTo(12));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(setup.Deck[i].Name, Is.EqualTo(expected[i]), $"牌库第 {i} 张保序");

            Assert.That(setup.EnemyHp, Is.EqualTo(30), "敌人 3002 领袖 HP");
            Assert.That(setup.EnemySummons.Count, Is.EqualTo(3), "小鬼×2 → 食人魔");
            Assert.That(setup.EnemySummons[0].Name, Is.EqualTo("小鬼"));
            Assert.That(setup.EnemySummons[2].Name, Is.EqualTo("食人魔"));
            Assert.That(setup.Cards, Is.SameAs(cards), "卡表注入（AddCardToHandOp 通路）");
        }

        [Test]
        public void T3_数据换敌_零代码生效()
        {
            CardCatalog cards = CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));
            DeckCatalog decks = DeckCatalog.Load(File.ReadAllText(TestDataPaths.DecksJson()), cards);
            EnemyCatalog enemies = EnemyCatalog.Load(File.ReadAllText(TestDataPaths.EnemiesJson()), cards);

            BattleSetup quick = DemoBattleData.CreateSetup(cards, decks.First, enemies.Get(3001), 0);
            BattleSetup boss = DemoBattleData.CreateSetup(cards, decks.First, enemies.Get(3003), 0);

            Assert.That(quick.EnemyHp, Is.EqualTo(22), "快攻型：低领袖 HP");
            Assert.That(boss.EnemyHp, Is.EqualTo(45), "Boss 型：高领袖 HP");
            Assert.That(quick.EnemySummons.Count, Is.EqualTo(6), "快攻型：高频小怪（小鬼×6）");
            Assert.That(boss.EnemySummons[1].Name, Is.EqualTo("食人魔"), "Boss 型：少而大");
        }

        [Test]
        public void T3_牌组引用未定义卡_加载即抛() =>
            Assert.That(() => DeckCatalog.Load("[{ \"id\": 9, \"name\": \"坏\", \"cards\": [{ \"id\": 9999 }] }]",
                    CardCatalog.Load("[" + Fireball + "]")),
                Throws.InvalidOperationException.With.Message.Contains("引用未定义的卡 id：9999"));

        [Test]
        public void T3_敌人召唤引用效果卡_加载即抛() =>
            Assert.That(() => EnemyCatalog.Load(
                    "[{ \"id\": 9, \"name\": \"坏敌\", \"leaderHp\": 10, \"summons\": [{ \"id\": 9004 }] }]",
                    CardCatalog.Load("[" + Fireball + "]")),
                Throws.InvalidOperationException.With.Message.Contains("召唤引用非法：9004"));

        // ==================== T4 量产卡（真 cards.json，每卡一用例） ====================

        static CardCatalog RealCards() => CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));

        [Test]
        public void T4_荆棘结界_打出后反弹攻击者()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1008 }, Monster("突袭者", 3, 5));
            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id);
            session.EndTurn();

            Assert.That(session.PlayerHp, Is.EqualTo(30 - 3));
            Assert.That(session.EnemyBoard[0].Attributes.CurrentInt(AttributeId.CurrentHp),
                Is.EqualTo(5 - 2), "荆棘结界：反弹 2 给攻击者");
        }

        [Test]
        public void T4_汲血印记_打击归因抽牌_他卡不触发()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1009, 1003, 1002, 1002, 1002, 1002 });
            session.Start(); // 起手 3 + 抽 1：[汲血印记, 打击, 电火花, 电火花]，牌库 2

            session.PlayEffectCard(0, session.EnemyLeader.Id); // 汲血印记：注册归因触发器（filter=打击 1003）
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 打击（1003）：归因命中 → 抽 1
            Assert.That(session.DeckCount, Is.EqualTo(1), "打击伤害归因 1003 → 汲血印记抽 1");

            session.EndTurn(); // 无召唤无攻击；回能量
            int deckBefore = session.DeckCount;
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 电火花（1002）：归因不匹配 → 不抽
            Assert.That(session.DeckCount, Is.EqualTo(deckBefore), "非打击卡的伤害不触发汲血印记");
        }

        [Test]
        public void T4_处决_击杀后回血自己()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1010 }, Monster("炮灰", 5, 5));
            session.Start();
            session.EndTurn(); // 领袖 30→25

            session.PlayEffectCard(0, session.EnemyBoard[0].Id); // 6 伤击杀 → LastDamageKilled → 回血 3

            Assert.That(session.EnemyBoard.Count, Is.EqualTo(0));
            Assert.That(session.PlayerHp, Is.EqualTo(25 + 3));
        }

        [Test]
        public void T4_学者_获得一张自身复制()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1011, 1011 });
            session.Start();

            session.PlayEffectCard(0, session.EnemyLeader.Id);

            Assert.That(session.Hand.Count, Is.EqualTo(2));
            Assert.That(session.Hand[1].Name, Is.EqualTo("学者"));
            Assert.That(session.Hand[1].Id, Is.EqualTo(1011));
        }

        [Test]
        public void T4_烈焰风暴_敌方全体各2_领袖不挨打()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1012 },
                Monster("小鬼A", 2, 3), Monster("小鬼B", 2, 3)); // 每敌方回合依序各召一只
            session.Start();
            session.EndTurn(); // 小鬼A 召唤并攻击：30→28
            session.EndTurn(); // 小鬼B 召唤；A、B 各攻击一次：28→24
            Assert.That(session.EnemyBoard.Count, Is.EqualTo(2));

            session.PlayEffectCard(0, session.EnemyLeader.Id); // 烈焰风暴：敌方全体 2

            Assert.That(session.EnemyBoard[0].Attributes.CurrentInt(AttributeId.CurrentHp), Is.EqualTo(1), "3-2");
            Assert.That(session.EnemyBoard[1].Attributes.CurrentInt(AttributeId.CurrentHp), Is.EqualTo(1), "3-2");
            Assert.That(session.PlayerHp, Is.EqualTo(24), "两回合共挨 6（每回合场上敌方各打一次）；AllOpposingUnits=敌方单位，敌方领袖不在目标内");
        }

        [Test]
        public void T4_治疗术_回复我方领袖()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1013 }, Monster("重锤", 5, 5));
            session.Start();
            session.EndTurn(); // 30→25

            session.PlayEffectCard(0, session.EnemyLeader.Id); // AlliedLeader 回 4

            Assert.That(session.PlayerHp, Is.EqualTo(29), "25+4（HealOp 首用，上限夹紧不触发）");
        }

        [Test]
        public void T4_战吼_我方全体攻击加一_两圈后回落()
        {
            BattleSession session = NewSession(RealCards(), new[] { 2001, 1014 });
            session.Start();
            session.SummonMonster(0); // 幼狼 2/2
            Assert.That(session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.Attack), Is.EqualTo(2));

            session.PlayEffectCard(0, session.EnemyLeader.Id); // 战吼：AllAlliedUnits +1 攻（Rounds=2）

            Assert.That(session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.Attack),
                Is.EqualTo(3), "光环面：修饰挂上即生效");

            session.EndTurn();
            session.EndTurn(); // 两个完整回合圈后到期回落
            Assert.That(session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.Attack),
                Is.EqualTo(2), "Rounds=2 到期，修饰回落");
        }

        [Test]
        public void T4_冰甲_预览减伤2()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1015 }, Monster("突袭者", 3, 3));
            session.Start();
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 冰甲：领袖受伤 -2（Preview 触发）

            session.EndTurn();

            Assert.That(session.PlayerHp, Is.EqualTo(30 - 1), "3 攻被冰甲 -2，只落 1");
        }

        [Test]
        public void T4_嘲讽图腾_敌方被迫攻击嘲讽单位()
        {
            BattleSession session = NewSession(RealCards(), new[] { 2001, 1016 }, Monster("突袭者", 1, 5));
            session.Start();
            session.SummonMonster(0); // 幼狼 2/2
            session.PlayEffectCard(0, session.EnemyLeader.Id); // 嘲讽图腾：我方全体获得嘲讽（含幼狼）

            session.EndTurn(); // 突袭者召出后被迫打幼狼（嘲讽规则），不打领袖

            Assert.That(session.PlayerHp, Is.EqualTo(30), "领袖被嘲讽保护");
            Assert.That(session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.CurrentHp),
                Is.EqualTo(1), "幼狼代吃 1 点（2/2→1）");
        }

        [Test]
        public void T4_雷击_随机敌方单体()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1017 }, Monster("小鬼", 2, 3));
            session.Start();
            session.EndTurn(); // 小鬼攻击：30→28

            session.PlayEffectCard(0, session.EnemyLeader.Id); // 唯一敌方单位 → 必中小鬼，4 伤致死

            Assert.That(session.EnemyBoard.Count, Is.EqualTo(0), "RandomOpposingUnit 词汇首用：3 血小鬼被 4 伤击杀");
            Assert.That(session.PlayerHp, Is.EqualTo(28), "此前小鬼打了领袖 2");
        }

        // ==================== T6 组装怪兽（SummonPayload 投影接线，纯 Battle 词汇） ====================

        static SummonPayload Payload(string name = "组装兽", int cost = 1, int attack = 3, int maxHp = 4,
            EffectGroupDef[] onSummon = null, EffectGroupDef[] onLeave = null,
            TriggerDef[] triggers = null, GameplayEffectDef[] auras = null, string[] grantTags = null) =>
            new SummonPayload
            {
                Name = name, Cost = cost, Attack = attack, MaxHp = maxHp,
                OnSummonEffects = onSummon, OnLeaveEffects = onLeave,
                ContinuousTriggers = triggers, Auras = auras, GrantTags = grantTags,
            };

        static BattleSession PayloadSession(SummonPayload[] payloadCards, params MonsterCardDef[] enemySummons)
        {
            BattleSetup setup = new BattleSetup { Cards = RealCards() };
            foreach (SummonPayload payload in payloadCards)
                setup.Deck.Add(new BattleCard { Id = 9001, Summon = payload });
            setup.EnemySummons.AddRange(enemySummons);
            return new BattleSession(setup);
        }

        [Test]
        public void T6_组装召唤_入场效果_光环_触发_标签_全接线()
        {
            // 卡套投影：3/4，入场对敌方领袖 2 伤；光环=我方全体+1 攻（含自己）；受伤反弹 1；随召唤获嘲讽
            SummonPayload payload = Payload(onSummon: new[]
                {
                    new EffectGroupDef { Target = TargetMode.OpposingLeader,
                        Ops = new EffectOp[] { new DamageOp { Amount = ScopedValue.Const(2) } } },
                },
                triggers: new[]
                {
                    new TriggerDef { EventTag = SkillTags.EventDamageApplied.Name, SelfOnly = true,
                        Effects = new EffectGroupDef { Target = TargetMode.Instigator,
                            Ops = new EffectOp[] { new DamageOp { Amount = ScopedValue.Const(1) } } } },
                },
                auras: new[]
                {
                    new GameplayEffectDef { Name = "群体威压", Duration = EffectDurationKind.Permanent,
                        Modifiers = new[] { new AttributeModifierDef { Attribute = AttributeId.Attack, Op = ModOp.Add,
                            Magnitude = ScopedValue.Const(1) } } },
                },
                grantTags: new[] { "Keyword.Taunt" });
            BattleSession session = PayloadSession(new[] { payload }, Monster("突袭者", 3, 3));
            session.Start();
            session.SummonMonster(0); // 能量 4：扣 1

            SkillEntity unit = session.PlayerBoard[0];
            Assert.That(session.EnemyHp, Is.EqualTo(30 - 2), "入场效果：对敌方领袖 2 伤（Source=新单位）");
            Assert.That(unit.Attributes.CurrentInt(AttributeId.Attack), Is.EqualTo(4), "光环含自己：3+1");
            Assert.That(unit.Tags.Has(SkillTags.KeywordTaunt), Is.True, "GrantTags 随召唤授予");

            session.EndTurn(); // 突袭者被迫打嘲讽单位（3 伤），反弹 1 给攻击者
            Assert.That(session.PlayerHp, Is.EqualTo(30), "嘲讽保护领袖");
            Assert.That(unit.Attributes.CurrentInt(AttributeId.CurrentHp), Is.EqualTo(1), "3/4 挨 3 伤");
            Assert.That(session.EnemyBoard[0].Attributes.CurrentInt(AttributeId.CurrentHp),
                Is.EqualTo(2), "SelfOnly 反弹触发器：1 伤回攻击者");
        }

        [Test]
        public void T6_组装召唤_光环随死亡撤除_幼狼回落()
        {
            SummonPayload payload = Payload(name: "威压核心", maxHp: 1, grantTags: new[] { "Keyword.Taunt" },
                auras: new[]
                {
                    new GameplayEffectDef { Name = "群体威压", Duration = EffectDurationKind.Permanent,
                        Modifiers = new[] { new AttributeModifierDef { Attribute = AttributeId.Attack, Op = ModOp.Add,
                            Magnitude = ScopedValue.Const(1) } } },
                });
            BattleSetup setup = new BattleSetup { Cards = RealCards() };
            setup.Deck.Add(setup.Cards.Get(2001));                          // 幼狼
            setup.Deck.Add(new BattleCard { Id = 9001, Summon = payload }); // 威压核心
            setup.EnemySummons.Add(Monster("突袭者", 2, 5));
            BattleSession session = new BattleSession(setup);
            session.Start();
            session.SummonMonster(0); // 幼狼 2/2
            session.SummonMonster(0); // 威压核心 3/1（嘲讽+光环）
            Assert.That(session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.Attack), Is.EqualTo(3), "幼狼吃光环 2+1");

            session.EndTurn(); // 突袭者(2,5)被迫打嘲讽的威压核心 → 1 血死亡 → 光环撤除 + 无离场效果
            Assert.That(session.PlayerBoard.Count, Is.EqualTo(1), "威压核心阵亡");
            Assert.That(session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.Attack),
                Is.EqualTo(2), "光环随死亡精准撤除（RemoveEffectsFromSource）");
        }

        [Test]
        public void T6_组装召唤_离场效果死亡时结算()
        {
            SummonPayload payload = Payload(name: "怨念体", attack: 1, maxHp: 1,
                grantTags: new[] { "Keyword.Taunt" },
                onLeave: new[]
                {
                    new EffectGroupDef { Target = TargetMode.OpposingLeader,
                        Ops = new EffectOp[] { new DamageOp { Amount = ScopedValue.Const(3) } } },
                });
            BattleSession session = PayloadSession(new[] { payload }, Monster("处刑者", 2, 5));
            session.Start();
            session.SummonMonster(0);

            session.EndTurn(); // 处刑者被迫打死嘲讽的怨念体 → 离场：敌方领袖 -3

            Assert.That(session.PlayerBoard.Count, Is.EqualTo(0));
            Assert.That(session.EnemyHp, Is.EqualTo(30 - 3), "OnLeave 槽：死亡时对敌方领袖 3 伤");
            Assert.That(session.PlayerHp, Is.EqualTo(30), "嘲讽代吃");
        }

        // ==================== M5 验收反馈：效果卡对己方释放 ====================

        [Test]
        public void 反馈_治疗术对己方领袖释放生效()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1013 }, Monster("重锤", 5, 5));
            session.Start();
            session.EndTurn(); // 领袖 30→25

            session.PlayEffectCard(0, session.PlayerLeader.Id); // 拖到自己场上=给领袖

            Assert.That(session.PlayerHp, Is.EqualTo(29), "AlliedLeader 类卡目标可为己方（MainTarget 仅作释放锚）");
        }

        [Test]
        public void 反馈_直伤卡不得以己方为目标()
        {
            BattleSession session = NewSession(RealCards(), new[] { 1003 }); // 打击（Chosen 直伤）
            session.Start();

            Assert.That(() => session.PlayEffectCard(0, session.PlayerLeader.Id),
                Throws.InvalidOperationException.With.Message.Contains("敌方"),
                "RequiresEnemyTarget 的卡维持敌方校验——防直伤卡烧自己");
            Assert.That(session.Hand.Count, Is.EqualTo(1), "命令失败不出牌不扣费");
        }

        [Test]
        public void 反馈_增益卡以己方单位为释放锚()
        {
            BattleSession session = NewSession(RealCards(), new[] { 2001, 1014 }); // 幼狼 + 战吼
            session.Start();
            session.SummonMonster(0);
            int unitId = session.PlayerBoard[0].Id;

            session.PlayEffectCard(0, unitId); // 拖到幼狼身上释放（锚=该怪）

            Assert.That(session.PlayerBoard[0].Attributes.CurrentInt(AttributeId.Attack),
                Is.EqualTo(3), "释放锚不改变结算目标（AllAlliedUnits 全体+1）");
        }

        // ==================== 构造辅助（DataDrivenCardTests 房风 + 卡表注入） ====================

        static CardCatalog FixtureCatalog(params string[] cards) =>
            CardCatalog.Load("[" + string.Join(",", cards) + "]");

        static BattleSession NewSession(CardCatalog catalog, int[] deckIds, params MonsterCardDef[] summons)
        {
            BattleSetup setup = new BattleSetup { Cards = catalog };
            if (summons != null) setup.EnemySummons.AddRange(summons);
            foreach (int id in deckIds) setup.Deck.Add(catalog.Get(id));
            return new BattleSession(setup);
        }

        static MonsterCardDef Monster(string name, int attack, int maxHp) =>
            new MonsterCardDef { Name = name, Attack = attack, MaxHp = maxHp };
    }
}
