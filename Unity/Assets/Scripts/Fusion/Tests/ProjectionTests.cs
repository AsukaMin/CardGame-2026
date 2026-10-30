using System;
using System.IO;
using Battle;
using NUnit.Framework;

namespace Fusion.Tests
{
    /// <summary>
    /// M5·T6 投影层与融合数据加载用例：MonsterSleeve → BattleCard（SummonPayload 臂）的数值/载荷聚合，
    /// 与 FusionCatalog 的加载校验（矩阵/重复 id/坏标签）。数据形状 = GameData/parts.json、sleeves.json。
    /// </summary>
    [TestFixture]
    public class ProjectionTests
    {
        [Test]
        public void 投影_数值与四路载荷聚合()
        {
            MonsterSleeve sleeve = SampleData.Build(SampleData.FighterSleeve(),
                SampleData.Claw(), SampleData.VenomGland(), SampleData.Grudge());

            BattleCard card = SleeveProjector.Compile(sleeve);
            SummonPayload payload = card.Summon;

            Assert.That(card.Id, Is.EqualTo(201), "卡身份=卡套 id（归因可查）");
            Assert.That(payload.Name, Is.EqualTo("石壳斗士"));
            Assert.That(payload.Cost, Is.EqualTo(2 + 2 + 1), "公式费：Σ部位费 ×1.0");
            Assert.That(payload.Attack, Is.EqualTo(3));
            Assert.That(payload.MaxHp, Is.EqualTo(2));
            Assert.That(payload.OnSummonEffects, Is.Not.Null.And.Length.EqualTo(1), "毒腺入场组");
            Assert.That(payload.OnLeaveEffects, Is.Not.Null.And.Length.EqualTo(1), "怨念离场组");
            Assert.That(payload.ContinuousTriggers, Is.Null, "无常驻槽位");
            Assert.That(payload.Auras, Is.Null);
            Assert.That(payload.Consumable, Is.True, "怨念是消耗卡");
        }

        [Test]
        public void 投影_豪华系数_先乘后取整()
        {
            MonsterSleeve sleeve = SampleData.Build(SampleData.LuxurySleeve(),
                SampleData.Claw(), SampleData.Shell(), SampleData.WarDrum());

            SummonPayload payload = SleeveProjector.Compile(sleeve).Summon;

            Assert.That(payload.Cost, Is.EqualTo(8), "round((2+1+3)×1.3)=round(7.8)=8（AwayFromZero）");
            Assert.That(payload.Attack, Is.EqualTo(5), "round((3+1)×1.3)=round(5.2)=5");
            Assert.That(payload.MaxHp, Is.EqualTo(8), "round((2+4)×1.3)=round(7.8)=8");
            Assert.That(payload.Auras, Is.Not.Null.And.Length.EqualTo(1), "战鼓光环进常驻聚合");
        }

        [Test]
        public void 投影_标签联合与常驻触发()
        {
            MonsterSleeve sleeve = SampleData.Build(SampleData.LuxurySleeve(),
                SampleData.TauntCarapace(), SampleData.Heart(), SampleData.WarHeart());

            SummonPayload payload = SleeveProjector.Compile(sleeve).Summon;

            Assert.That(payload.GrantTags, Is.EqualTo(new[] { "Keyword.Taunt" }), "GrantTags 跨槽位联合");
            Assert.That(payload.ContinuousTriggers, Is.Not.Null.And.Length.EqualTo(1), "战意常驻触发");
            Assert.That(payload.Attack, Is.EqualTo(3), "round((0+2)×1.3)=round(2.6)=3");
        }

        [Test]
        public void 投影_缺身材槽拒绝()
        {
            MonsterSleeve empty = new MonsterSleeve(SampleData.FighterSleeve());
            Assert.That(() => SleeveProjector.Compile(empty),
                Throws.InvalidOperationException.With.Message.Contains("缺少身材槽"));
        }

        // ==================== FusionCatalog（数据加载入口） ====================

        const string MiniParts =
            "[{ \"id\": 101, \"name\": \"利爪\", \"slotType\": \"Body\", \"cost\": 2, \"attack\": 3, \"maxHp\": 2 }," +
            " { \"id\": 111, \"name\": \"毒腺\", \"slotType\": \"OnSummon\", \"cost\": 2," +
            "   \"effects\": [ { \"target\": \"RandomOpposingUnit\"," +
            "     \"ops\": [ { \"op\": \"Damage\", \"amount\": { \"kind\": \"Const\", \"value\": 2.0 }, \"kind\": \"Effect\" } ] } ] }]";
        const string MiniSleeves =
            "[{ \"id\": 201, \"name\": \"石壳斗士\", \"slots\": [ \"Body\", \"OnSummon\", \"OnLeave\" ]," +
            "  \"cost\": { \"multiplier\": 1.0, \"flat\": 0 }, \"statMultiplier\": 1.0 }]";

        [Test]
        public void 加载_部位与卡套可查()
        {
            FusionCatalog catalog = FusionCatalog.Load(MiniParts, MiniSleeves);
            Assert.That(catalog.Parts.Count, Is.EqualTo(2));
            Assert.That(catalog.Sleeves.Count, Is.EqualTo(1));
            Assert.That(catalog.GetPart(111).Name, Is.EqualTo("毒腺"));
            Assert.That(catalog.GetSleeve(201).Slots.Length, Is.EqualTo(3));
        }

        [Test]
        public void 加载_矩阵违例即抛() =>
            Assert.That(() => FusionCatalog.Load(
                    "[{ \"id\": 105, \"name\": \"坏身材\", \"slotType\": \"Body\", \"attack\": 1, \"maxHp\": 1," +
                    "  \"effects\": [ { \"target\": \"Self\", \"ops\": [ { \"op\": \"Heal\", \"amount\": { \"kind\": \"Const\", \"value\": 1.0 } } ] } ] }]",
                    MiniSleeves),
                Throws.InvalidOperationException.With.Message.Contains("不得携带行为载荷"));

        [Test]
        public void 加载_重复id即抛() =>
            Assert.That(() => FusionCatalog.Load(
                    "[{ \"id\": 101, \"name\": \"A\", \"slotType\": \"Body\", \"attack\": 1, \"maxHp\": 1 }," +
                    " { \"id\": 101, \"name\": \"B\", \"slotType\": \"Body\", \"attack\": 1, \"maxHp\": 1 }]",
                    MiniSleeves),
                Throws.InvalidOperationException.With.Message.Contains("部位卡 id 重复"));

        [Test]
        public void 加载_坏标签即抛() =>
            Assert.That(() => FusionCatalog.Load(
                    "[{ \"id\": 106, \"name\": \"坏标签\", \"slotType\": \"Body\", \"attack\": 1, \"maxHp\": 1," +
                    "  \"grantTags\": [ \"Nope.X\" ] }]",
                    MiniSleeves),
                Throws.InstanceOf<Exception>().With.Message.Contains("Nope.X"));

        [Test]
        public void 加载_真文件_parts与sleeves全量通过()
        {
            // 双通道真文件回归：仓库根 GameData/parts.json + sleeves.json（手写 JSON 的加载验证）
            string parts = File.ReadAllText(FindGameData("parts.json"));
            string sleeves = File.ReadAllText(FindGameData("sleeves.json"));
            FusionCatalog catalog = FusionCatalog.Load(parts, sleeves);

            Assert.That(catalog.Parts.Count, Is.EqualTo(8), "利爪/甲壳/心脏/嘲讽甲壳/毒腺/战鼓/怨念/战意");
            Assert.That(catalog.Sleeves.Count, Is.EqualTo(2), "石壳斗士/豪华熔核体");

            // 真数据端到端：石壳斗士 + 利爪 + 毒腺 + 怨念 → 投影可组出合法战斗卡
            MonsterSleeve sleeve = new MonsterSleeve(catalog.GetSleeve(201));
            sleeve.Install(0, catalog.GetPart(101));
            sleeve.Install(1, catalog.GetPart(111));
            sleeve.Install(2, catalog.GetPart(113));
            BattleCard card = SleeveProjector.Compile(sleeve);
            Assert.That(card.Summon.Cost, Is.EqualTo(5));
            Assert.That(card.Summon.OnLeaveEffects, Is.Not.Null.And.Length.EqualTo(1), "怨念离场组");
        }

        /// <summary>从测试程序集所在目录向上找仓库根 GameData（与 Battle.Tests.TestDataPaths 同款双通道定位——
        /// headless 在 .zcode/tmp/…/bin、EditMode 在 Library/ScriptAssemblies，向上走均命中仓库根；
        /// 注意 AppContext.BaseDirectory 在编辑器下指向 Unity 安装目录，不能用作起点）。</summary>
        static string FindGameData(string fileName)
        {
            string assemblyDir = Path.GetDirectoryName(typeof(ProjectionTests).Assembly.Location);
            for (DirectoryInfo dir = new DirectoryInfo(string.IsNullOrEmpty(assemblyDir) ? AppContext.BaseDirectory : assemblyDir);
                 dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "GameData", fileName);
                if (File.Exists(candidate)) return candidate;
            }
            throw new InvalidOperationException($"未找到 GameData/{fileName}");
        }
    }
}
