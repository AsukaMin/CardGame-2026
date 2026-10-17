using System.Collections.Generic;
using Battle;
using NUnit.Framework;

namespace CardAuthoring.Tests
{
    /// <summary>图编译器与 JSON 序列化对的用例（零 UnityEngine，headless 可跑）。</summary>
    [TestFixture]
    public class GraphCompilerTests
    {
        // ---------- 构图辅助 ----------

        static EffectDefNodeData Def(string guid, TargetMode target = TargetMode.Chosen) =>
            new EffectDefNodeData { guid = guid, target = target };

        static OpNodeData Op(string guid, EffectOp op)
        {
            OpNodeData n = new OpNodeData { guid = guid };
            n.SetOp(op);
            return n;
        }

        static ConnectionData Conn(string from, string port, string to) =>
            new ConnectionData { outputNodeGuid = from, outputPort = port, inputNodeGuid = to };

        // ---------- 图编译 ----------

        [Test]
        public void 链编译_顺序与目标模式()
        {
            var defs = new List<EffectDefNodeData> { Def("def1", TargetMode.AllEnemyUnits) };
            var ops = new List<OpNodeData>
            {
                Op("n1", new DamageOp { Amount = 6 }),
                Op("n2", new DrawCardsOp { Count = 1 }),
            };
            var conns = new List<ConnectionData>
            {
                Conn("def1", PortNames.Ops, "n1"),
                Conn("n1", PortNames.Next, "n2"),
            };

            GraphCompiler.Result r = GraphCompiler.Compile(defs, ops, conns);

            Assert.AreEqual(0, r.Warnings.Count);
            Assert.AreEqual(1, r.Effects.Count);
            Assert.AreEqual(TargetMode.AllEnemyUnits, r.Effects[0].Target);
            Assert.AreEqual(2, r.Effects[0].Ops.Length);
            Assert.IsInstanceOf<DamageOp>(r.Effects[0].Ops[0]);
            Assert.AreEqual(6, ((DamageOp)r.Effects[0].Ops[0]).Amount);
            Assert.IsInstanceOf<DrawCardsOp>(r.Effects[0].Ops[1]);
            Assert.AreEqual(1, ((DrawCardsOp)r.Effects[0].Ops[1]).Count);
        }

        [Test]
        public void 条件分支_是否两侧递归()
        {
            var defs = new List<EffectDefNodeData> { Def("def1") };
            var ops = new List<OpNodeData>
            {
                Op("c1", new ConditionalOp { Condition = new TargetHpBelowPercentCondition { Percent = 0.5 } }),
                Op("hi", new DamageOp { Amount = 6 }),
                Op("lo", new DamageOp { Amount = 3 }),
            };
            var conns = new List<ConnectionData>
            {
                Conn("def1", PortNames.Ops, "c1"),
                Conn("c1", PortNames.Then, "hi"),
                Conn("c1", PortNames.Else, "lo"),
            };

            GraphCompiler.Result r = GraphCompiler.Compile(defs, ops, conns);

            Assert.AreEqual(0, r.Warnings.Count);
            ConditionalOp cond = (ConditionalOp)r.Effects[0].Ops[0];
            Assert.AreEqual(0.5, ((TargetHpBelowPercentCondition)cond.Condition).Percent);
            Assert.AreEqual(1, cond.Then.Length);
            Assert.AreEqual(6, ((DamageOp)cond.Then[0]).Amount);
            Assert.AreEqual(1, cond.Else.Length);
            Assert.AreEqual(3, ((DamageOp)cond.Else[0]).Amount);
        }

        [Test]
        public void 嵌套条件_分支内再分支()
        {
            var defs = new List<EffectDefNodeData> { Def("def1") };
            var ops = new List<OpNodeData>
            {
                Op("c1", new ConditionalOp { Condition = new TargetIsFullHpCondition() }),
                Op("c2", new ConditionalOp { Condition = new TargetHpBelowPercentCondition { Percent = 0.3 } }),
                Op("d1", new DamageOp { Amount = 1 }),
            };
            var conns = new List<ConnectionData>
            {
                Conn("def1", PortNames.Ops, "c1"),
                Conn("c1", PortNames.Then, "c2"),
                Conn("c2", PortNames.Else, "d1"),
            };

            GraphCompiler.Result r = GraphCompiler.Compile(defs, ops, conns);

            Assert.AreEqual(0, r.Warnings.Count);
            ConditionalOp outer = (ConditionalOp)r.Effects[0].Ops[0];
            ConditionalOp inner = (ConditionalOp)outer.Then[0];
            Assert.AreEqual(1, inner.Else.Length);
            Assert.AreEqual(0, inner.Then.Length);
        }

        [Test]
        public void 孤立节点_跳过并告警()
        {
            var defs = new List<EffectDefNodeData> { Def("def1") };
            var ops = new List<OpNodeData>
            {
                Op("n1", new DamageOp { Amount = 6 }),
                Op("orphan", new HealOp { Amount = 4 }),
            };
            var conns = new List<ConnectionData> { Conn("def1", PortNames.Ops, "n1") };

            GraphCompiler.Result r = GraphCompiler.Compile(defs, ops, conns);

            Assert.AreEqual(1, r.Effects[0].Ops.Length);
            Assert.AreEqual(1, r.Warnings.Count);
            StringAssert.Contains("孤立", r.Warnings[0]);
        }

        [Test]
        public void 环防护_不死循环()
        {
            var defs = new List<EffectDefNodeData> { Def("def1") };
            var ops = new List<OpNodeData>
            {
                Op("n1", new DamageOp { Amount = 1 }),
                Op("n2", new HealOp { Amount = 1 }),
            };
            var conns = new List<ConnectionData>
            {
                Conn("def1", PortNames.Ops, "n1"),
                Conn("n1", PortNames.Next, "n2"),
                Conn("n2", PortNames.Next, "n1"), // 环
            };

            GraphCompiler.Result r = GraphCompiler.Compile(defs, ops, conns);

            Assert.IsNotEmpty(r.Warnings);
            Assert.LessOrEqual(r.Effects[0].Ops.Length, 2); // 截断而非死循环
        }

        [Test]
        public void 空图_空效果无告警()
        {
            GraphCompiler.Result r = GraphCompiler.Compile(
                new List<EffectDefNodeData>(), new List<OpNodeData>(), new List<ConnectionData>());
            Assert.AreEqual(0, r.Effects.Count);
            Assert.AreEqual(0, r.Warnings.Count);
        }

        // ---------- JSON 序列化对 ----------

        [Test]
        public void JSON往返_单个操作()
        {
            string json = CardJsonWriter.WriteEffectOp(new DamageOp { Amount = 6 });
            StringAssert.Contains("\"op\": \"Damage\"", json);

            DamageOp back = (DamageOp)CardJsonReader.ParseEffectOp(json);
            Assert.AreEqual(6, back.Amount);
        }

        [Test]
        public void JSON往返_条件嵌套操作树()
        {
            var original = new ConditionalOp
            {
                Condition = new TargetHpBelowPercentCondition { Percent = 0.5 },
                Then = new EffectOp[] { new DamageOp { Amount = 6 }, new HealOp { Amount = 2 } },
                Else = new EffectOp[] { new DamageOp { Amount = 3 } },
            };

            ConditionalOp back = (ConditionalOp)CardJsonReader.ParseEffectOp(CardJsonWriter.WriteEffectOp(original));

            Assert.AreEqual(0.5, ((TargetHpBelowPercentCondition)back.Condition).Percent);
            Assert.AreEqual(2, back.Then.Length);
            Assert.AreEqual(6, ((DamageOp)back.Then[0]).Amount);
            Assert.AreEqual(2, ((HealOp)back.Then[1]).Amount);
            Assert.AreEqual(1, back.Else.Length);
        }

        [Test]
        public void JSON往返_效果卡池()
        {
            var cards = new List<EffectCardConfig>
            {
                new EffectCardConfig
                {
                    Id = 301, Name = "火球术",
                    Tiers = new[]
                    {
                        new EffectTier { Cost = 2, Label = "低配", Effects = new[]
                        {
                            new EffectDef { Target = TargetMode.Chosen, Ops = new EffectOp[] { new DamageOp { Amount = 3 } } },
                        } },
                        new EffectTier { Cost = 4, Label = "完全体", Effects = new[]
                        {
                            new EffectDef { Target = TargetMode.Chosen, Ops = new EffectOp[]
                            {
                                new ConditionalOp
                                {
                                    Condition = new TargetHpBelowPercentCondition { Percent = 0.5 },
                                    Then = new EffectOp[] { new DamageOp { Amount = 6 } },
                                    Else = new EffectOp[] { new DamageOp { Amount = 3 } },
                                },
                            } },
                        } },
                    },
                },
                new EffectCardConfig { Id = 300, Name = "排序用", Tiers = new EffectTier[0] },
            };

            List<EffectCardConfig> back = CardJsonReader.ParseEffectCards(CardJsonWriter.WriteEffectCards(cards));

            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(300, back[0].Id, "导出必须按 id 升序稳定排序");
            Assert.AreEqual(301, back[1].Id);
            Assert.AreEqual("火球术", back[1].Name);
            Assert.AreEqual(2, back[1].Tiers.Length);
            Assert.AreEqual(2, back[1].Tiers[0].Cost);
            Assert.AreEqual("低配", back[1].Tiers[0].Label);
            Assert.AreEqual(3, ((DamageOp)back[1].Tiers[0].Effects[0].Ops[0]).Amount);
            ConditionalOp cond = (ConditionalOp)back[1].Tiers[1].Effects[0].Ops[0];
            Assert.AreEqual(6, ((DamageOp)cond.Then[0]).Amount);
            Assert.AreEqual(3, ((DamageOp)cond.Else[0]).Amount);
        }

        [Test]
        public void JSON往返_部位卡池()
        {
            var parts = new List<PartCardConfig>
            {
                new PartCardConfig
                {
                    Id = 104, Name = "嘲讽甲壳", SlotType = SlotType.Body, Cost = 2,
                    Attack = 0, MaxHp = 5, GrantKeywords = Keywords.Taunt,
                    Effects = new[]
                    {
                        new EffectDef { Target = TargetMode.AllFriendlyUnits, Ops = new EffectOp[]
                        {
                            new GrantKeywordOp { Keyword = Keywords.Taunt, Duration = ModifierDurationKind.Turns, TurnCount = 2 },
                        } },
                    },
                },
                new PartCardConfig { Id = 113, Name = "怨念", SlotType = SlotType.OnLeave, Cost = 1, Consumable = true, MaxHp = 0 },
            };

            List<PartCardConfig> back = CardJsonReader.ParsePartCards(CardJsonWriter.WritePartCards(parts));

            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(104, back[0].Id);
            Assert.AreEqual(SlotType.Body, back[0].SlotType);
            Assert.AreEqual(Keywords.Taunt, back[0].GrantKeywords);
            GrantKeywordOp grant = (GrantKeywordOp)back[0].Effects[0].Ops[0];
            Assert.AreEqual(ModifierDurationKind.Turns, grant.Duration);
            Assert.AreEqual(2, grant.TurnCount);
            Assert.IsTrue(back[1].Consumable);
        }

        [Test]
        public void JSON判别键_命名规则稳定()
        {
            string condJson = CardJsonWriter.WriteEffectOp(new ConditionalOp
            {
                Condition = new TargetHpBelowPercentCondition(),
            });
            StringAssert.Contains("\"condition\": \"TargetHpBelowPercent\"", condJson);
            StringAssert.Contains("\"op\": \"Conditional\"", condJson);
        }
    }
}
