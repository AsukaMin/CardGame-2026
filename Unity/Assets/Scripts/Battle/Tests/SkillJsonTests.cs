using System;
using Battle;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>
    /// T1 多态序列化层用例：判别字段读写 / 往返稳定（文本对照法，不给 POCO 加 Equals）/
    /// 加载期校验抛错矩阵（未知判别值/非法枚举名/未知标签名）/ 宿主 op 进 JSON。
    /// 标签只用 SkillTags 预声明名——巡检前置（宿主标签须先 Declare 再反序列化）不在本层用例范围。
    /// </summary>
    [TestFixture]
    public class SkillJsonTests
    {
        static bool hostOpRegistered;

        [OneTimeSetUp]
        public void RegisterHostOp()
        {
            if (hostOpRegistered) return;
            SkillJson.RegisterOp<LogOp>("Log"); // 宿主自定义 op 进 JSON 的最小先例（T4 抽牌卡同款路径）
            hostOpRegistered = true;
        }

        // ==================== 往返 ====================

        [Test]
        public void 往返_伤害op_判别字段与全字段保真()
        {
            DamageOp damage = new DamageOp { Amount = ScopedValue.Const(6), Kind = DamageSourceKind.Combat };

            string json = RoundTrip(damage, out DamageOp copy);

            StringAssert.Contains("\"op\": \"Damage\"", json);
            StringAssert.Contains("\"kind\": \"Combat\"", json);
            StringAssert.Contains("\"value\": 6.0", json);
            Assert.That(copy.Amount.Value, Is.EqualTo(6));
            Assert.That(copy.Kind, Is.EqualTo(DamageSourceKind.Combat));
        }

        [Test]
        public void 往返_八种内置op_混合嵌套保真()
        {
            EffectGroupDef[] groups = new[]
            {
                new EffectGroupDef
                {
                    Target = TargetMode.AllOpposingUnits,
                    Ops = new EffectOp[]
                    {
                        new DamageOp { Amount = ScopedValue.Const(6) },
                        new HealOp
                        {
                            Amount = new ScopedValue
                            {
                                Kind = ValueSourceKind.SourceAttribute, Attribute = AttributeId.MaxHp, Coefficient = 0.5,
                            },
                        },
                        new ModifyAttributeOp
                        {
                            Attribute = AttributeId.Attack, Op = ModOp.Multiply, Magnitude = ScopedValue.Const(2),
                            Duration = EffectDurationKind.Rounds, Rounds = 2,
                        },
                        new GrantTagOp { Tag = SkillTags.KeywordTaunt.Name, Duration = EffectDurationKind.Rounds, Rounds = 1 },
                        new RemoveTagOp { Tag = SkillTags.KeywordPoison.Name },
                        new ApplyEffectOp { Effect = PoisonEffect() },
                        new ConditionalOp
                        {
                            Condition = new HasTagCondition { Tag = SkillTags.KeywordTaunt.Name, Exact = true },
                            ConditionTarget = ConditionTargetKind.Source,
                            Then = new EffectOp[]
                            {
                                new DamageOp { Amount = new ScopedValue { Kind = ValueSourceKind.EventValue } },
                                new ConditionalOp
                                {
                                    Condition = new AttributeCompareCondition
                                    {
                                        Attribute = AttributeId.CurrentHp, Op = CompareOperator.LessOrEqual,
                                        ValueType = CompareValueType.PercentOfMaxHp, Value = 50,
                                    },
                                },
                            },
                            Else = new EffectOp[] { new HealOp { Amount = ScopedValue.Const(3) } },
                        },
                        new ModifyEventMagnitudeOp { Delta = -2, Scale = 0.5 },
                    },
                    Cues = new[] { new CueDef { Cue = "Hit", Param = 1 } },
                    PlayAfterAnimation = true,
                },
            };

            string json = RoundTrip(groups, out EffectGroupDef[] copy);

            StringAssert.Contains("\"target\": \"AllOpposingUnits\"", json);
            StringAssert.Contains("\"mod\": \"Multiply\"", json); // ModOp 字段让位判别字段 "op" → JSON 键名 "mod"
            Assert.That(copy[0].Ops.Length, Is.EqualTo(8));
            ConditionalOp conditional = (ConditionalOp)copy[0].Ops[6];
            Assert.That(conditional.Then.Length, Is.EqualTo(2));
            Assert.That(((HasTagCondition)conditional.Condition).Tag, Is.EqualTo("Keyword.Taunt"));
            ApplyEffectOp apply = (ApplyEffectOp)copy[0].Ops[5];
            Assert.That(apply.Effect.PeriodicEffects.Ops.Length, Is.EqualTo(1)); // 深递归位（ApplyEffect→周期组→Ops）
            Assert.That(apply.Effect.StackBy, Is.EqualTo(StackAggregation.BySource));
        }

        [Test]
        public void 往返_三种条件_根数组保真()
        {
            ConditionDef[] conditions = new ConditionDef[]
            {
                new AttributeCompareCondition
                {
                    Attribute = AttributeId.MaxHp, Op = CompareOperator.Greater,
                    ValueType = CompareValueType.PercentOfMaxHp, Value = 50,
                },
                new EventValueCondition { Op = CompareOperator.Equal, Value = 3 },
                new HasTagCondition { Tag = SkillTags.KeywordLifesteal.Name, Exact = true },
            };

            string json = RoundTrip(conditions, out ConditionDef[] copy);

            StringAssert.Contains("\"kind\": \"HasTag\"", json);
            StringAssert.Contains("\"op\": \"Greater\"", json); // 条件层比较运算符字段就叫 "op"（判别字段是 "kind"，无冲突）
            Assert.That(copy[0], Is.InstanceOf<AttributeCompareCondition>());
            Assert.That(((EventValueCondition)copy[1]).Value, Is.EqualTo(3));
            Assert.That(((HasTagCondition)copy[2]).Exact, Is.True);
        }

        [Test]
        public void 往返_ScopedValue四形态保真()
        {
            DamageOp[] ops = new[]
            {
                new DamageOp { Amount = ScopedValue.Const(6) },
                new DamageOp { Amount = new ScopedValue { Kind = ValueSourceKind.EventValue } },
                new DamageOp { Amount = new ScopedValue { Kind = ValueSourceKind.SetByCaller, Key = "charge", Value = 9 } },
                new DamageOp
                {
                    Amount = new ScopedValue
                    {
                        Kind = ValueSourceKind.SourceAttribute, Attribute = AttributeId.MaxHp, Coefficient = 2.5,
                    },
                },
            };

            RoundTrip(ops, out DamageOp[] copy);

            Assert.That(copy[1].Amount.Kind, Is.EqualTo(ValueSourceKind.EventValue));
            Assert.That(copy[2].Amount.Key, Is.EqualTo("charge"));
            Assert.That(copy[3].Amount.Coefficient, Is.EqualTo(2.5));
            Assert.That(copy[3].Amount.Attribute, Is.EqualTo(AttributeId.MaxHp));
        }

        [Test]
        public void 往返_空数组与null缺省语义()
        {
            EffectGroupDef group = new EffectGroupDef
            {
                Ops = new EffectOp[]
                {
                    new ConditionalOp { Then = new EffectOp[0], Else = new EffectOp[] { new HealOp() } },
                },
            };

            string json = RoundTrip(group, out EffectGroupDef copy);

            StringAssert.Contains("\"then\": []", json);    // 空数组照写
            StringAssert.DoesNotContain("\"cues\"", json);  // null 位省略（缺省读入 = 字段默认值）
            ConditionalOp conditional = (ConditionalOp)copy.Ops[0];
            Assert.That(conditional.Then, Is.Empty);
            Assert.That(conditional.Else.Length, Is.EqualTo(1));
        }

        [Test]
        public void 手写最小JSON_省略字段取默认值()
        {
            DamageOp damage = SkillJson.Deserialize<DamageOp>(
                "{ \"op\": \"Damage\", \"amount\": { \"kind\": \"Const\", \"value\": 6 } }");
            Assert.That(damage.Amount.Value, Is.EqualTo(6));
            Assert.That(damage.Amount.Coefficient, Is.EqualTo(1.0)); // 字段默认值即合理默认
            Assert.That(damage.Kind, Is.EqualTo(DamageSourceKind.Effect));
        }

        [Test]
        public void 幂等_同一对象两次序列化文本一致()
        {
            EffectOp[] ops = new EffectOp[] { new DamageOp { Amount = ScopedValue.Const(4) } };
            Assert.That(SkillJson.Serialize(ops), Is.EqualTo(SkillJson.Serialize(ops)));
        }

        // ==================== 抛错矩阵（加载期校验，不带病运行） ====================

        [Test]
        public void 抛错_未知op判别值() =>
            AssertThrowsContaining("未知操作判别值", () => SkillJson.Deserialize<EffectOp>("{ \"op\": \"Nuke\" }"));

        [Test]
        public void 抛错_缺失op判别字段() =>
            AssertThrowsContaining("缺少判别字段",
                () => SkillJson.Deserialize<EffectOp>("{ \"amount\": { \"kind\": \"Const\" } }"));

        [Test]
        public void 抛错_未知条件kind() =>
            AssertThrowsContaining("未知条件判别值",
                () => SkillJson.Deserialize<ConditionDef>("{ \"kind\": \"AlwaysTrue\" }"));

        [Test]
        public void 抛错_非法枚举名() =>
            AssertThrowsContaining("NotASource",
                () => SkillJson.Deserialize<DamageOp>("{ \"op\": \"Damage\", \"amount\": { \"kind\": \"NotASource\" } }"));

        [Test]
        public void 抛错_枚举数字被拒() =>
            Assert.Catch(() => SkillJson.Deserialize<DamageOp>("{ \"op\": \"Damage\", \"amount\": { \"kind\": 1 } }"),
                "枚举位给数字应被拒绝（StringEnumConverter AllowIntegerValues=false）");

        [Test]
        public void 抛错_未知标签名() =>
            AssertThrowsContaining("未声明的标签",
                () => SkillJson.Deserialize<EffectOp>("{ \"op\": \"GrantTag\", \"tag\": \"Nope.Undefined\" }"));

        [Test]
        public void 抛错_空标签名() =>
            AssertThrowsContaining("标签名为空",
                () => SkillJson.Deserialize<EffectOp>("{ \"op\": \"GrantTag\" }"));

        [Test]
        public void 抛错_持续效果标签数组位报错带数据路径() =>
            AssertThrowsContaining("grantedTags[1]", () => SkillJson.Deserialize<ApplyEffectOp>(
                "{ \"op\": \"ApplyEffect\", \"effect\": { \"grantedTags\": [ \"Keyword.Poison\", \"Typo.Bogus\" ] } }"));

        // ==================== 宿主自定义 op ====================

        [Test]
        public void 宿主op_登记后进JSON_往返保真()
        {
            LogOp log = new LogOp { Message = "hello" };

            string json = RoundTrip(log, out LogOp copy);

            StringAssert.Contains("\"op\": \"Log\"", json);
            Assert.That(copy.Message, Is.EqualTo("hello"));
        }

        [Test]
        public void 宿主op_未登记类型序列化抛错() =>
            AssertThrowsContaining("未登记判别名", () => SkillJson.Serialize(new RemoveEntityOp()));

        [Test]
        public void 宿主op_判别名冲突登记抛错() =>
            Assert.Throws<InvalidOperationException>(() => SkillJson.RegisterOp<RaiseEventOp>("Damage"));

        [Test]
        public void 宿主op_同类型重复登记抛错() =>
            Assert.Throws<InvalidOperationException>(() => SkillJson.RegisterOp<LogOp>("LogAgain"));

        // ==================== 辅助 ====================

        /// <summary>文本对照法断言往返稳定：POCO→JSON₁→POCO'→JSON₂，两份 JSON 必须一致（同数据进出一致）。</summary>
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

        static GameplayEffectDef PoisonEffect() => new GameplayEffectDef
        {
            Name = "测试毒",
            AssetTags = new[] { SkillTags.KeywordPoison.Name },
            GrantedTags = new[] { SkillTags.KeywordPoison.Name },
            Duration = EffectDurationKind.Rounds,
            Rounds = 3,
            PeriodicEachRound = true,
            PeriodicEffects = new EffectGroupDef
            {
                Target = TargetMode.Self,
                Ops = new EffectOp[] { new DamageOp { Amount = ScopedValue.Const(1) } },
            },
            Modifiers = new[] { new AttributeModifierDef { Op = ModOp.Add, Magnitude = ScopedValue.Const(1) } },
            StackBy = StackAggregation.BySource,
            StackLimit = 3,
            StackRefreshDuration = false,
            ExpirePerStack = true,
        };
    }
}
