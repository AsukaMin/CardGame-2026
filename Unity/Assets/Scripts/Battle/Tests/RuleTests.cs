using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>规则级用例：费用公式、身材系数、充能档位、嘲讽、攻击限制、回复框架、生命周期、意图。</summary>
    [TestFixture]
    public class RuleTests
    {
        static BattleSetup BasicSetup(int points = 10, List<EffectCardConfig> deck = null)
        {
            return new BattleSetup
            {
                Tuning = new BattleTuning { InitialActionPoints = points },
                DeckCards = deck ?? new List<EffectCardConfig>(),
                PlayerRoster =
                {
                    SampleData.Build(SampleData.FighterSleeve(), SampleData.Claw(), SampleData.VenomGland(), SampleData.Grudge()),
                },
                Enemy = SampleData.BasicEnemy(),
                Seed = 12345,
            };
        }

        static int FirstPlayerUnitId(BattleSession battle) => battle.GetSnapshot().Units[0].Id;

        // ---------- 费用公式 ----------

        [Test]
        public void 召唤费_乘数定值与下限()
        {
            // 斗士 [身(利爪2)][入(毒腺2)][离(怨念1)] = 5 费
            MonsterSleeve sleeve = SampleData.Build(
                SampleData.FighterSleeve(), SampleData.Claw(), SampleData.VenomGland(), SampleData.Grudge());
            Assert.AreEqual(5, sleeve.SummonCost);

            // ×0.75：round(3.75) = 4
            MonsterSleeve discount = SampleData.Build(
                SampleData.Sleeve(210, "折扣型", new[] { SlotType.Body, SlotType.OnSummon, SlotType.OnLeave }, costMult: 0.75),
                SampleData.Claw(), SampleData.VenomGland(), SampleData.Grudge());
            Assert.AreEqual(4, discount.SummonCost);

            // 定值 -2：5 - 2 = 3
            MonsterSleeve flat = SampleData.Build(
                SampleData.Sleeve(211, "减费型", new[] { SlotType.Body, SlotType.OnSummon, SlotType.OnLeave }, costFlat: -2),
                SampleData.Claw(), SampleData.VenomGland(), SampleData.Grudge());
            Assert.AreEqual(3, flat.SummonCost);

            // 下限 0：甲壳1 + 怨念1 - 2 = 0
            MonsterSleeve floor = SampleData.Build(
                SampleData.Sleeve(212, "白嫖型", new[] { SlotType.Body, SlotType.OnLeave }, costFlat: -2),
                SampleData.Shell(), SampleData.Grudge());
            Assert.AreEqual(0, floor.SummonCost);
        }

        [Test]
        public void 召唤费_先乘后加_四舍五入()
        {
            // 利爪 2 费 × 0.75 = 1.5 → AwayFromZero → 2
            MonsterSleeve s = SampleData.Build(
                SampleData.Sleeve(213, "七五折", new[] { SlotType.Body }, costMult: 0.75), SampleData.Claw());
            Assert.AreEqual(2, s.SummonCost);
        }

        [Test]
        public void 身材_求和后乘系数()
        {
            MonsterSleeve normal = SampleData.Build(
                SampleData.Sleeve(214, "标准型", new[] { SlotType.Body, SlotType.Body }),
                SampleData.Claw(), SampleData.Shell());
            Assert.AreEqual(4, normal.BaseStats.attack); // 3+1
            Assert.AreEqual(6, normal.BaseStats.maxHp);  // 2+4

            // 豪华型 ×1.3：round(4×1.3)=5，round(6×1.3)=8
            MonsterSleeve luxury = SampleData.Build(
                SampleData.LuxurySleeve(), SampleData.Claw(), SampleData.Shell(), SampleData.WarDrum());
            Assert.AreEqual(5, luxury.BaseStats.attack);
            Assert.AreEqual(8, luxury.BaseStats.maxHp);
        }

        [Test]
        public void 防御性校验_缺身材部位则拒绝()
        {
            MonsterSleeve bad = SampleData.Build(
                SampleData.Sleeve(215, "空壳", new[] { SlotType.OnSummon }), SampleData.VenomGland());
            Assert.Throws<InvalidOperationException>(bad.Validate);

            BattleSetup setup = BasicSetup();
            setup.PlayerRoster.Clear();
            setup.PlayerRoster.Add(bad);
            Assert.Throws<InvalidOperationException>(() => new BattleSession(setup));
        }

        // ---------- 充能档位（手打面值） ----------

        [Test]
        public void 充能档位_低配与完全体面值扣费()
        {
            BattleSetup setup = BasicSetup(points: 6,
                deck: new List<EffectCardConfig> { SampleData.Fireball(), SampleData.Fireball() });
            setup.FixedDeckOrder = new List<int> { 301, 301 };
            BattleSession battle = new BattleSession(setup);
            Assert.AreEqual(6, battle.ActionPoints);

            // 低配 2 费：3 伤
            Assert.IsTrue(battle.PlayCard(0, 0, BattleSession.EnemyHeroId).Success);
            Assert.AreEqual(4, battle.ActionPoints);
            Assert.AreEqual(17, battle.EnemyHp); // 20 - 3

            // 完全体 4 费：6 伤
            Assert.IsTrue(battle.PlayCard(0, 1, BattleSession.EnemyHeroId).Success);
            Assert.AreEqual(0, battle.ActionPoints);
            Assert.AreEqual(11, battle.EnemyHp);
        }

        [Test]
        public void 充能档位_点数不足则失败且不扣费()
        {
            BattleSetup setup = BasicSetup(points: 3, deck: new List<EffectCardConfig> { SampleData.Fireball() });
            BattleSession battle = new BattleSession(setup);

            PlayResult result = battle.PlayCard(0, 1, BattleSession.EnemyHeroId); // 需要 4
            Assert.IsFalse(result.Success);
            Assert.AreEqual(FailReason.NotEnoughPoints, result.Reason);
            Assert.AreEqual(3, battle.ActionPoints);
            Assert.AreEqual(20, battle.EnemyHp);
            Assert.AreEqual(1, battle.HandCount); // 卡仍在手
        }

        [Test]
        public void 手打面值_不因卡套折扣而变()
        {
            // 场上站着 75% 折扣卡套召唤的单位，手牌火球仍按面值 2 费扣
            BattleSetup setup = BasicSetup(points: 10, deck: new List<EffectCardConfig> { SampleData.Fireball() });
            setup.FixedDeckOrder = new List<int> { 301 };
            setup.PlayerRoster.Add(SampleData.Build(
                SampleData.Sleeve(210, "折扣型", new[] { SlotType.Body }, costMult: 0.75), SampleData.Claw()));

            BattleSession battle = new BattleSession(setup);
            Assert.IsTrue(battle.SummonMonster(1).Success); // 折扣召唤 round(2×0.75)=2
            Assert.IsTrue(battle.PlayCard(0, 0, BattleSession.EnemyHeroId).Success);
            Assert.AreEqual(10 - 2 - 2, battle.ActionPoints); // 面值 2，不吃折扣
        }

        [Test]
        public void 条件效果_半血以下伤害翻倍()
        {
            BattleSetup setup = BasicSetup(points: 10,
                deck: new List<EffectCardConfig> { SampleData.Execute(), SampleData.Execute() });
            setup.FixedDeckOrder = new List<int> { 303, 303 };
            BattleSession battle = new BattleSession(setup);

            // 注入 10/20 的敌方单位（恰好半血）
            var half = new BattleUnit { Id = 500, Owner = Side.Enemy, Name = "半血怪", BaseAttack = 1, BaseMaxHp = 20, Hp = 10, Sleeve = SampleData.EnemyBruteSleeve() };
            battle.Field.Add(half);

            // 满血主战者 → 低配 3 伤
            Assert.IsTrue(battle.PlayCard(0, 0, BattleSession.EnemyHeroId).Success);
            Assert.AreEqual(17, battle.EnemyHp);

            // 半血单位 → 完全体 6 伤
            Assert.IsTrue(battle.PlayCard(0, 0, 500).Success);
            Assert.AreEqual(4, battle.Field.GetUnit(500).Hp);
        }

        // ---------- 攻击与嘲讽 ----------

        [Test]
        public void 召唤当回合可攻击_每回合限一次()
        {
            BattleSetup setup = BasicSetup();
            setup.Enemy.CastPool = new EffectDef[0][];
            BattleSession battle = new BattleSession(setup);
            Assert.IsTrue(battle.SummonMonster(0).Success);
            int unitId = FirstPlayerUnitId(battle);

            // 无召唤失调：当回合即可攻击敌方主战者
            Assert.IsTrue(battle.Attack(unitId, BattleSession.EnemyHeroId).Success);

            PlayResult second = battle.Attack(unitId, BattleSession.EnemyHeroId);
            Assert.IsFalse(second.Success);
            Assert.AreEqual(FailReason.AlreadyAttacked, second.Reason);

            // 固定下回合意图为攻击玩家，避免随机意图击杀测试单位
            battle.EnemyIntentsInternal[0] = new AttackPlayerIntent { Damage = 4 };
            battle.EndTurn(); // 敌方回合后回到玩家回合
            Assert.AreEqual(BattlePhase.PlayerMain, battle.Phase);
            Assert.IsTrue(battle.Attack(unitId, BattleSession.EnemyHeroId).Success);
        }

        [Test]
        public void 敌方AI_尊重玩家嘲讽()
        {
            Battlefield field = new Battlefield();
            field.Add(new BattleUnit { Id = 100, Owner = Side.Player, Name = "嘲讽怪", BaseMaxHp = 5, Hp = 5, BaseKeywords = Keywords.Taunt });
            field.Add(new BattleUnit { Id = 101, Owner = Side.Player, Name = "普通怪", BaseMaxHp = 5, Hp = 5 });
            EnemyAI ai = new EnemyAI(SampleData.BasicEnemy(), new Rng(7));

            for (int i = 0; i < 10; i++)
            {
                EffectTarget target = ai.PickUnitAttackTarget(field);
                Assert.IsTrue(target.IsUnit);
                Assert.AreEqual(100, target.Unit.Id, "存在嘲讽时只能打嘲讽怪");
            }
        }

        [Test]
        public void 嘲讽强制优先_打脸与非嘲讽被拒()
        {
            BattleSession battle = new BattleSession(BasicSetup());
            Assert.IsTrue(battle.SummonMonster(0).Success);
            int unitId = FirstPlayerUnitId(battle);

            // 注入一个敌方嘲讽单位（绕过意图直接摆上场面）
            var enemyTaunt = new BattleUnit
            {
                Id = 500, Owner = Side.Enemy, Name = "敌方嘲讽", BaseAttack = 2, BaseMaxHp = 4, Hp = 4,
                BaseKeywords = Keywords.Taunt, Sleeve = SampleData.EnemyBruteSleeve(),
            };
            battle.Field.Add(enemyTaunt);

            PlayResult face = battle.Attack(unitId, BattleSession.EnemyHeroId);
            Assert.IsFalse(face.Success);
            Assert.AreEqual(FailReason.TauntViolation, face.Reason);

            PlayResult missing = battle.Attack(unitId, 999);
            Assert.AreEqual(FailReason.InvalidTarget, missing.Reason);

            // 嘲讽死亡后打脸恢复自由（本回合尚未攻击过）
            battle.ApplyDamage(new EffectTarget(enemyTaunt), 99);
            Assert.IsTrue(battle.Attack(unitId, BattleSession.EnemyHeroId).Success);
        }

        [Test]
        public void 互殴同时结算_双方死亡进墓地()
        {
            BattleSession battle = new BattleSession(BasicSetup());
            Assert.IsTrue(battle.SummonMonster(0).Success); // 斗士：利爪 → 3/2
            int unitId = FirstPlayerUnitId(battle);

            var mirror = new BattleUnit { Id = 500, Owner = Side.Enemy, Name = "镜像", BaseAttack = 3, BaseMaxHp = 2, Hp = 2, Sleeve = SampleData.EnemyBruteSleeve() };
            battle.Field.Add(mirror);
            Assert.IsTrue(battle.Attack(unitId, 500).Success);

            Assert.AreEqual(0, battle.Field.AllUnits.Count); // 同归于尽
            Assert.AreEqual(2, battle.GetSnapshot().GraveyardPackageCount);
            Assert.AreEqual(17, battle.EnemyHp); // 我方怨念离场：20 - 3
        }

        // ---------- 回复框架 ----------

        [Test]
        public void 回复框架_三钩子触发并可发点()
        {
            var policy = new SampleData.ScriptedRecoveryPolicy
            {
                PointsPerTurnStart = 1, PointsPerAttack = 1, PointsPerDiscard = 1,
            };
            BattleSetup setup = BasicSetup(points: 5, deck: new List<EffectCardConfig> { SampleData.Fireball() });
            setup.Recovery = policy;
            BattleSession battle = new BattleSession(setup);

            Assert.AreEqual(1, policy.TurnStartCalls);  // 开局即第 1 回合钩子
            Assert.AreEqual(6, battle.ActionPoints);    // 5 + 1

            Assert.IsTrue(battle.SummonMonster(0).Success); // 5 费
            Assert.IsTrue(battle.Attack(FirstPlayerUnitId(battle), BattleSession.EnemyHeroId).Success);
            Assert.AreEqual(1, policy.AttackCalls);
            Assert.AreEqual(2, battle.ActionPoints);    // 6 - 5 + 1

            Assert.IsTrue(battle.Discard(0).Success);
            Assert.AreEqual(1, policy.DiscardCalls);
            Assert.AreEqual(3, battle.ActionPoints);    // 2 + 1
        }

        [Test]
        public void 行动点跨回合不清零()
        {
            BattleSession battle = new BattleSession(BasicSetup(points: 7));
            battle.EndTurn();
            Assert.AreEqual(7, battle.ActionPoints);
            battle.EndTurn();
            Assert.AreEqual(7, battle.ActionPoints);
        }

        // ---------- 生命周期 ----------

        [Test]
        public void 死亡整包进墓地_离场触发_消耗标记()
        {
            BattleSession battle = new BattleSession(BasicSetup(points: 10));
            Assert.IsTrue(battle.SummonMonster(0).Success); // [身(利爪)][入(毒腺)][离(怨念)]
            int unitId = FirstPlayerUnitId(battle);
            int enemyHpBefore = battle.EnemyHp;

            battle.ApplyDamage(new EffectTarget(battle.Field.GetUnit(unitId)), 5); // 2 血，击杀

            Assert.AreEqual(enemyHpBefore - 3, battle.EnemyHp); // 怨念离场：对敌方主战者 3 伤
            Assert.AreEqual(0, battle.Field.AllUnits.Count);
            Assert.AreEqual(1, battle.GetSnapshot().GraveyardPackageCount);

            battle.ApplyDamage(new EffectTarget(Side.Enemy), 999); // 强制终局
            BattleResult result = battle.Result;
            Assert.IsNotNull(result);
            // 怨念=消耗 → 销毁；利爪/毒腺 → 回收
            CollectionAssert.AreEquivalent(new[] { 101, 111 }, result.RecoverablePartCardIds);
            CollectionAssert.AreEquivalent(new[] { 113 }, result.DestroyedPartCardIds);
        }

        [Test]
        public void 效果卡打出即消失_不入墓地()
        {
            BattleSetup setup = BasicSetup(points: 6, deck: new List<EffectCardConfig> { SampleData.Fireball() });
            BattleSession battle = new BattleSession(setup);
            Assert.AreEqual(1, battle.HandCount);

            Assert.IsTrue(battle.PlayCard(0, 0, BattleSession.EnemyHeroId).Success);
            Assert.AreEqual(0, battle.HandCount);
            Assert.AreEqual(0, battle.GetSnapshot().GraveyardPackageCount);
        }

        [Test]
        public void 常驻光环_随离场失效()
        {
            BattleSetup setup = BasicSetup(points: 20);
            setup.PlayerRoster.Add(SampleData.Build(
                SampleData.LuxurySleeve(), SampleData.Claw(), SampleData.Shell(), SampleData.WarDrum()));
            BattleSession battle = new BattleSession(setup);

            // 豪华型（利爪+甲壳=4/6 ×1.3 → 5/8；消耗 round(6×1.3)=8）
            Assert.IsTrue(battle.SummonMonster(1).Success);
            Assert.AreEqual(6, battle.GetSnapshot().Units[0].Attack); // 5 + 1（战鼓含自身）

            Assert.IsTrue(battle.SummonMonster(0).Success); // 斗士 3/2 → 受战鼓 +1
            var fighter = battle.Field.UnitsOf(Side.Player).Find(u => u.BaseAttack == 3);
            Assert.IsNotNull(fighter);
            Assert.AreEqual(4, fighter.EffectiveAttack);

            // 击杀战鼓载体 → 光环失效
            battle.ApplyDamage(new EffectTarget(battle.Field.AllUnits[0]), 99);
            Assert.AreEqual(1, battle.Field.AllUnits.Count);
            Assert.AreEqual(3, battle.Field.GetUnit(fighter.Id).EffectiveAttack);
        }

        // ---------- 意图 ----------

        [Test]
        public void 意图_开局即揭示且每回合刷新()
        {
            BattleSession battle = new BattleSession(BasicSetup());
            Assert.AreEqual(1, battle.EnemyIntents.Count);
            Assert.IsNotNull(battle.EnemyIntents[0]);

            battle.EndTurn();
            Assert.AreEqual(1, battle.EnemyIntents.Count); // 敌方回合后揭示下回合意图
        }

        [Test]
        public void 意图目标死亡_重定向到玩家()
        {
            BattleSetup setup = BasicSetup(points: 10);
            setup.EnemySummonPool.Clear();
            setup.Enemy.CastPool = new EffectDef[0][];
            BattleSession battle = new BattleSession(setup);

            Assert.IsTrue(battle.SummonMonster(0).Success);
            int unitId = FirstPlayerUnitId(battle);
            battle.EnemyIntentsInternal[0] = new AttackUnitIntent { TargetUnitId = unitId, Damage = 4 };
            battle.ApplyDamage(new EffectTarget(battle.Field.GetUnit(unitId)), 99); // 目标先死

            int playerHp = battle.PlayerHp;
            battle.EndTurn();
            Assert.AreEqual(playerHp - 4, battle.PlayerHp); // 重定向到玩家本人
        }
    }
}
