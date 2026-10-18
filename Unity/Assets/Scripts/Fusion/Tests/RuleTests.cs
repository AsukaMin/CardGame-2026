using System;
using NUnit.Framework;

namespace Fusion.Tests
{
    /// <summary>融合规则级用例：召唤费公式、身材系数、装槽约束、防御性校验（战斗会话用例随战斗代码移除，待重写）。</summary>
    [TestFixture]
    public class RuleTests
    {
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

        // ---------- 身材 ----------

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

        // ---------- 装槽与校验 ----------

        [Test]
        public void 装槽_槽型不匹配与槽位占用则拒绝()
        {
            MonsterSleeve sleeve = SampleData.Build(
                SampleData.Sleeve(218, "单槽型", new[] { SlotType.Body }), SampleData.Claw());

            // 槽型不匹配：毒腺（入场槽）装不进身材槽
            Assert.Throws<InvalidOperationException>(() => sleeve.Install(0, SampleData.VenomGland()));
            // 槽位已占用：第二张利爪无处可装
            Assert.Throws<InvalidOperationException>(() => sleeve.Install(0, SampleData.Claw()));
            // 索引越界
            Assert.Throws<ArgumentOutOfRangeException>(() => sleeve.Install(1, SampleData.Claw()));
        }

        [Test]
        public void 装槽_内容矩阵违例则拒绝()
        {
            // 身材槽部位不得带行为载荷
            PartCardConfig bodyWithBehavior = new PartCardConfig
            {
                Id = 120, Name = "错位爪", SlotType = SlotType.Body, Cost = 1, Attack = 2, MaxHp = 2,
                Effects = new[] { new Battle.EffectGroupDef() },
            };
            MonsterSleeve s1 = new MonsterSleeve(SampleData.Sleeve(219, "单身槽", new[] { SlotType.Body }));
            Assert.Throws<InvalidOperationException>(() => s1.Install(0, bodyWithBehavior));

            // 效果槽部位不得带身材数值
            PartCardConfig effectWithBody = new PartCardConfig
            {
                Id = 121, Name = "带肉的毒腺", SlotType = SlotType.OnSummon, Cost = 1, Attack = 1,
            };
            MonsterSleeve s2 = new MonsterSleeve(SampleData.Sleeve(220, "入槽", new[] { SlotType.OnSummon }));
            Assert.Throws<InvalidOperationException>(() => s2.Install(0, effectWithBody));

            // 常驻槽部位的行为应走 Triggers/Auras，不用 Effects
            PartCardConfig continuousWithEffects = new PartCardConfig
            {
                Id = 122, Name = "走错门的战鼓", SlotType = SlotType.Continuous, Cost = 1,
                Effects = new[] { new Battle.EffectGroupDef() },
            };
            MonsterSleeve s3 = new MonsterSleeve(SampleData.Sleeve(221, "常驻槽", new[] { SlotType.Continuous }));
            Assert.Throws<InvalidOperationException>(() => s3.Install(0, continuousWithEffects));
        }

        [Test]
        public void 防御性校验_缺身材部位则拒绝()
        {
            MonsterSleeve bad = SampleData.Build(
                SampleData.Sleeve(215, "空壳", new[] { SlotType.OnSummon }), SampleData.VenomGland());
            Assert.Throws<InvalidOperationException>(bad.Validate);
        }
    }
}
