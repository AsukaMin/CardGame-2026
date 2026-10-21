using System;
using System.IO;
using Battle;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>T3 加载入口用例：CardCatalog 解析/查询/id 唯一巡检 + 真 GameData/cards.json 双通道加载。</summary>
    [TestFixture]
    public class CardCatalogTests
    {
        const string TwoCardsJson =
            "[{\"kind\":\"effect\",\"id\":1001,\"name\":\"打击\",\"cost\":1," +
            "\"effects\":[{\"ops\":[{\"op\":\"Damage\",\"amount\":{\"kind\":\"Const\",\"value\":3}}]}]}," +
            "{\"kind\":\"monster\",\"id\":2001,\"name\":\"幼狼\",\"cost\":2,\"attack\":2,\"maxHp\":2}]";

        [Test]
        public void 解析_查询与计数()
        {
            CardCatalog catalog = CardCatalog.Load(TwoCardsJson);

            Assert.That(catalog.Count, Is.EqualTo(2));
            Assert.That(catalog.Get(1001).Effect.Name, Is.EqualTo("打击"));
            Assert.That(catalog.Get(2001).Monster.Attack, Is.EqualTo(2));
            Assert.That(catalog.TryGet(9999, out _), Is.False);
        }

        [Test]
        public void 解析_未定义id抛异常() =>
            Assert.Throws<InvalidOperationException>(() => CardCatalog.Load("[]").Get(1001));

        [Test]
        public void 解析_id重复抛异常() =>
            Assert.Throws<InvalidOperationException>(() => CardCatalog.Load(
                "[{\"kind\":\"monster\",\"id\":2001,\"name\":\"甲\",\"maxHp\":1}," +
                "{\"kind\":\"monster\",\"id\":2001,\"name\":\"乙\",\"maxHp\":1}]"));

        [Test]
        public void 解析_空表合法() =>
            Assert.That(CardCatalog.Load("[]").Count, Is.EqualTo(0));

        [Test]
        public void 真文件_仓库GameData卡表加载()
        {
            CardCatalog catalog = CardCatalog.Load(File.ReadAllText(TestDataPaths.CardsJson()));

            Assert.That(catalog.Count, Is.GreaterThanOrEqualTo(6), "M1 六卡全量在表");
            Assert.That(catalog.Get(1001).Effect.Name, Is.EqualTo("火球术"));
            Assert.That(catalog.Get(2001).Monster.MaxHp, Is.EqualTo(2));
        }
    }
}
