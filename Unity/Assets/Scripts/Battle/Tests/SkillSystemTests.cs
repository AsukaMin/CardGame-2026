using System;
using System.Collections.Generic;
using Battle;
using NUnit.Framework;

namespace Battle.Tests
{
    /// <summary>技能系统规则级用例：标签/属性/持续效果/触发/引擎契约（测试宿主=内存世界+死亡观察）。</summary>
    [TestFixture]
    public class SkillSystemTests
    {
        // ---------- 便捷构造 ----------

        static EffectGroupDef Group(TargetMode target, params EffectOp[] ops) =>
            new EffectGroupDef { Target = target, Ops = ops };

        static DamageOp Dmg(double v) => new DamageOp { Amount = ScopedValue.Const(v) };

        static HealOp Heal(double v) => new HealOp { Amount = ScopedValue.Const(v) };

        static ModifyAttributeOp StatAdd(AttributeId attr, double v,
            EffectDurationKind duration = EffectDurationKind.Permanent, int rounds = 1) =>
            new ModifyAttributeOp
            {
                Attribute = attr, Op = ModOp.Add, Magnitude = ScopedValue.Const(v),
                Duration = duration, Rounds = rounds,
            };

        static TriggerDef Trig(string tag, EffectGroupDef effects,
            TriggerTiming timing = TriggerTiming.After, bool once = false, bool selfOnly = false) =>
            new TriggerDef
            {
                EventTag = tag, Effects = effects, Timing = timing, Once = once, SelfOnly = selfOnly,
            };

        // ==================== 标签 ====================

        [Test]
        public void 标签_层级匹配与精确匹配()
        {
            SkillTag c = TagRegistry.Declare("Test.Deep.Child");
            SkillTag parent = TagRegistry.Resolve("Test.Deep");
            SkillTag root = TagRegistry.Resolve("Test");

            Assert.IsTrue(c.Matches(parent), "子标签拥有父标签");
            Assert.IsTrue(c.Matches(root), "子标签拥有根标签");
            Assert.IsFalse(parent.Matches(c), "父标签不拥有子标签");
            Assert.IsTrue(c.MatchesExact(c));
            Assert.IsFalse(c.MatchesExact(parent));

            TagContainer container = new TagContainer();
            container.Add(c);
            Assert.IsTrue(container.Has(parent), "容器层级匹配");
            Assert.IsFalse(container.HasExact(parent), "容器精确匹配不命中");
        }

        [Test]
        public void 标签_引用计数_最后来源移除才摘除()
        {
            TagContainer container = new TagContainer();
            SkillTag tag = TagRegistry.Declare("Test.RefCount");
            int added = 0, removed = 0;
            container.OnTagAdded += _ => added++;
            container.OnTagRemoved += _ => removed++;

            container.Add(tag);
            container.Add(tag);
            Assert.IsTrue(container.Has(tag));
            Assert.AreEqual(1, added, "重复 Add 只触发一次 Added");

            container.Remove(tag);
            Assert.IsTrue(container.Has(tag), "还有一份来源，标签不摘除");
            Assert.AreEqual(0, removed);

            container.Remove(tag);
            Assert.IsFalse(container.Has(tag), "最后一个来源移除，标签真正消失");
            Assert.AreEqual(1, removed);
        }

        [Test]
        public void 标签_未声明解析抛异常()
        {
            Assert.Throws<InvalidOperationException>(() => TagRegistry.Resolve("Test.NeverDeclared"));
        }

        // ==================== 属性 ====================

        [Test]
        public void 属性_双值与聚合顺序_先加后乘_Override覆盖()
        {
            SkillAttributeContainer attrs = new SkillAttributeContainer();
            attrs.SetBase(AttributeId.Attack, 10);

            attrs.AddModifier(new AttributeModifier { Attribute = AttributeId.Attack, Op = ModOp.Add, Magnitude = 2 });
            attrs.AddModifier(new AttributeModifier { Attribute = AttributeId.Attack, Op = ModOp.Multiply, Magnitude = 1.5 });
            Assert.AreEqual(18, attrs.Current(AttributeId.Attack), "(10+2)×1.5——先加后乘");
            Assert.AreEqual(10, attrs.Base(AttributeId.Attack), "Base 不受修饰影响");

            attrs.AddModifier(new AttributeModifier { Attribute = AttributeId.Attack, Op = ModOp.Override, Magnitude = 99 });
            Assert.AreEqual(99, attrs.Current(AttributeId.Attack), "Override 直接覆盖");
        }

        [Test]
        public void 属性_堆叠参与聚合_Add乘层数_乘法线性叠乘()
        {
            SkillAttributeContainer attrs = new SkillAttributeContainer();
            attrs.SetBase(AttributeId.Attack, 5);
            attrs.AddModifier(new AttributeModifier
            {
                Attribute = AttributeId.Attack, Op = ModOp.Add, Magnitude = 10, StackCount = 3,
            });
            Assert.AreEqual(35, attrs.Current(AttributeId.Attack), "Add×层数：5+10×3");

            SkillAttributeContainer attrs2 = new SkillAttributeContainer();
            attrs2.SetBase(AttributeId.Attack, 10);
            attrs2.AddModifier(new AttributeModifier
            {
                Attribute = AttributeId.Attack, Op = ModOp.Multiply, Magnitude = 1.1, StackCount = 3,
            });
            Assert.AreEqual(13, attrs2.Current(AttributeId.Attack), 1e-9, "线性叠乘：10×(1+0.1×3)=13");
        }

        [Test]
        public void 属性_MaxHp联动_增益抬血_减益夹紧_治疗封顶()
        {
            SkillAttributeContainer attrs = new SkillAttributeContainer();
            attrs.SetBase(AttributeId.MaxHp, 10);
            attrs.SetBase(AttributeId.CurrentHp, 10);

            attrs.SetBase(AttributeId.MaxHp, 12);
            Assert.AreEqual(12, attrs.Current(AttributeId.CurrentHp), "上限增益同步抬血");

            attrs.SetBase(AttributeId.MaxHp, 8);
            Assert.AreEqual(8, attrs.Current(AttributeId.CurrentHp), "上限减益夹紧当前血");

            attrs.SetBase(AttributeId.CurrentHp, 99);
            Assert.AreEqual(8, attrs.Current(AttributeId.CurrentHp), "治疗不超过上限");
        }

        // ==================== 持续效果 ====================

        [Test]
        public void 效果_永久修改直改Base()
        {
            TestHost host = new TestHost();
            SkillEntity u = host.AddUnit(TestHost.TeamA, "猛将", 5, 10);
            host.Engine.Execute(
                new[] { Group(TargetMode.Self, StatAdd(AttributeId.Attack, 3)) },
                SkillContext.Of(u));
            Assert.AreEqual(8, u.Attributes.Current(AttributeId.Attack));
            Assert.AreEqual(8, u.Attributes.Base(AttributeId.Attack), "永久修改直改 Base");
        }

        [Test]
        public void 效果_限时修饰到期回落()
        {
            TestHost host = new TestHost();
            SkillEntity u = host.AddUnit(TestHost.TeamA, "猛将", 5, 10);
            host.Engine.Execute(
                new[] { Group(TargetMode.Self, StatAdd(AttributeId.Attack, 2, EffectDurationKind.Rounds, 2)) },
                SkillContext.Of(u));
            Assert.AreEqual(7, u.Attributes.Current(AttributeId.Attack));

            host.BeginRound();
            Assert.AreEqual(7, u.Attributes.Current(AttributeId.Attack), "还剩 1 轮");

            host.BeginRound();
            Assert.AreEqual(5, u.Attributes.Current(AttributeId.Attack), "到期回落");
        }

        [Test]
        public void 效果_叠层与上限与时长刷新()
        {
            TestHost host = new TestHost();
            SkillEntity caster = host.AddUnit(TestHost.TeamA, "施法者", 1, 10);
            SkillEntity target = host.AddUnit(TestHost.TeamB, "目标", 5, 10);

            GameplayEffectDef buff = new GameplayEffectDef
            {
                Name = "削弱",
                Duration = EffectDurationKind.Rounds,
                Rounds = 1,
                StackLimit = 2,
                Modifiers = new[] { new AttributeModifierDef
                {
                    Attribute = AttributeId.Attack, Op = ModOp.Add, Magnitude = ScopedValue.Const(1),
                } },
            };

            for (int i = 0; i < 3; i++)
                host.Engine.Execute(
                    new[] { Group(TargetMode.Chosen, new ApplyEffectOp { Effect = buff }) },
                    new SkillContext { Source = caster, MainTarget = target });

            Assert.AreEqual(1, target.Effects.Active.Count, "第三层被上限拒绝，同类叠进同一个实例");
            Assert.AreEqual(2, target.Effects.Active[0].StackCount, "叠到 2 层");
            Assert.AreEqual(7, target.Attributes.Current(AttributeId.Attack), "5+1×2 层");

            host.BeginRound();
            Assert.AreEqual(5, target.Attributes.Current(AttributeId.Attack), "Rounds=1 到期整效果移除");
            Assert.AreEqual(0, target.Effects.Active.Count);
        }

        [Test]
        public void 效果_到期掉层_ExpirePerStack()
        {
            TestHost host = new TestHost();
            SkillEntity caster = host.AddUnit(TestHost.TeamA, "施法者", 1, 10);
            SkillEntity target = host.AddUnit(TestHost.TeamB, "目标", 5, 10);

            GameplayEffectDef buff = new GameplayEffectDef
            {
                Name = "剧毒叠层",
                Duration = EffectDurationKind.Rounds,
                Rounds = 1,
                ExpirePerStack = true,
                Modifiers = new[] { new AttributeModifierDef
                {
                    Attribute = AttributeId.Attack, Op = ModOp.Add, Magnitude = ScopedValue.Const(1),
                } },
            };
            for (int i = 0; i < 2; i++)
                host.Engine.Execute(
                    new[] { Group(TargetMode.Chosen, new ApplyEffectOp { Effect = buff }) },
                    new SkillContext { Source = caster, MainTarget = target });
            Assert.AreEqual(7, target.Attributes.Current(AttributeId.Attack));

            host.BeginRound();
            Assert.AreEqual(6, target.Attributes.Current(AttributeId.Attack), "掉一层剩一层");

            host.BeginRound();
            Assert.AreEqual(5, target.Attributes.Current(AttributeId.Attack), "最后一层到期移除");
        }

        [Test]
        public void 效果_周期毒_每回合跳伤_到期停止()
        {
            TestHost host = new TestHost();
            SkillEntity caster = host.AddUnit(TestHost.TeamA, "施毒者", 1, 10);
            SkillEntity victim = host.AddUnit(TestHost.TeamB, "中毒者", 1, 10);

            GameplayEffectDef poison = new GameplayEffectDef
            {
                Name = "毒",
                Duration = EffectDurationKind.Rounds,
                Rounds = 3,
                PeriodicEachRound = true,
                PeriodicEffects = Group(TargetMode.Self, Dmg(1)),
            };
            host.Engine.Execute(
                new[] { Group(TargetMode.Chosen, new ApplyEffectOp { Effect = poison }) },
                new SkillContext { Source = caster, MainTarget = victim });

            host.BeginRound();
            Assert.AreEqual(9, victim.Attributes.Current(AttributeId.CurrentHp));
            host.BeginRound();
            Assert.AreEqual(8, victim.Attributes.Current(AttributeId.CurrentHp));
            host.BeginRound();
            Assert.AreEqual(7, victim.Attributes.Current(AttributeId.CurrentHp), "第三跳后到期");

            host.BeginRound();
            Assert.AreEqual(7, victim.Attributes.Current(AttributeId.CurrentHp), "到期不再跳伤");
        }

        [Test]
        public void 效果_免疫与前置门禁()
        {
            TestHost host = new TestHost();
            SkillTag shield = TagRegistry.Declare("Test.Shield");
            TagRegistry.Declare("Test.NotPresent"); // 已声明但目标身上没有——前置缺失走门禁，而非配置报错
            SkillEntity caster = host.AddUnit(TestHost.TeamA, "施法者", 1, 10);
            SkillEntity immune = host.AddUnit(TestHost.TeamB, "免疫者", 5, 10);
            immune.Tags.Add(shield);

            GameplayEffectDef debuff = new GameplayEffectDef
            {
                Name = "诅咒",
                Duration = EffectDurationKind.Permanent,
                ApplicationImmunityTags = new[] { shield.Name },
                Modifiers = new[] { new AttributeModifierDef
                {
                    Attribute = AttributeId.Attack, Op = ModOp.Add, Magnitude = ScopedValue.Const(-2),
                } },
            };
            host.Engine.Execute(
                new[] { Group(TargetMode.Chosen, new ApplyEffectOp { Effect = debuff }) },
                new SkillContext { Source = caster, MainTarget = immune });
            Assert.AreEqual(5, immune.Attributes.Current(AttributeId.Attack), "免疫：效果未挂上");

            GameplayEffectDef required = new GameplayEffectDef
            {
                Name = "需要护盾",
                Duration = EffectDurationKind.Permanent,
                ApplicationRequiredTags = new[] { "Test.NotPresent" },
                Modifiers = new[] { new AttributeModifierDef
                {
                    Attribute = AttributeId.Attack, Op = ModOp.Add, Magnitude = ScopedValue.Const(1),
                } },
            };
            host.Engine.Execute(
                new[] { Group(TargetMode.Chosen, new ApplyEffectOp { Effect = required }) },
                new SkillContext { Source = caster, MainTarget = immune });
            Assert.AreEqual(5, immune.Attributes.Current(AttributeId.Attack), "前置缺失：效果未挂上");
        }

        [Test]
        public void 效果_精准撤销与标签引用计数_两来源同标签()
        {
            TestHost host = new TestHost();
            SkillEntity caster1 = host.AddUnit(TestHost.TeamA, "施法者甲", 1, 10);
            SkillEntity caster2 = host.AddUnit(TestHost.TeamA, "施法者乙", 1, 10);
            SkillEntity target = host.AddUnit(TestHost.TeamB, "目标", 5, 10);

            GameplayEffectDef MakeBuff(string name) => new GameplayEffectDef
            {
                Name = name,
                Duration = EffectDurationKind.Permanent,
                GrantedTags = new[] { "Keyword.Taunt" },
                Modifiers = new[] { new AttributeModifierDef
                {
                    Attribute = AttributeId.Attack, Op = ModOp.Add, Magnitude = ScopedValue.Const(1),
                } },
            };
            GameplayEffectDef buff1 = MakeBuff("buff1");
            GameplayEffectDef buff2 = MakeBuff("buff2");

            host.Engine.Execute(new[] { Group(TargetMode.Chosen, new ApplyEffectOp { Effect = buff1 }) },
                new SkillContext { Source = caster1, MainTarget = target });
            host.Engine.Execute(new[] { Group(TargetMode.Chosen, new ApplyEffectOp { Effect = buff2 }) },
                new SkillContext { Source = caster2, MainTarget = target });

            Assert.AreEqual(7, target.Attributes.Current(AttributeId.Attack));
            Assert.IsTrue(target.HasTag("Keyword.Taunt"), "两来源都给了嘲讽标签");

            host.Engine.RemoveEffectsFromSource(caster1);
            Assert.AreEqual(6, target.Attributes.Current(AttributeId.Attack), "只撤销了甲的效果");
            Assert.IsTrue(target.HasTag("Keyword.Taunt"), "乙还在给标签——引用计数保护");

            host.Engine.RemoveEffectsFromSource(caster2);
            Assert.AreEqual(5, target.Attributes.Current(AttributeId.Attack));
            Assert.IsFalse(target.HasTag("Keyword.Taunt"), "最后一个来源撤除，标签消失");
        }

        [Test]
        public void 光环_来源撤除即失效()
        {
            TestHost host = new TestHost();
            SkillEntity leader = host.AddLeader(TestHost.TeamA, "领袖", 30);
            SkillEntity u1 = host.AddUnit(TestHost.TeamA, "甲", 3, 10);
            SkillEntity u2 = host.AddUnit(TestHost.TeamA, "乙", 4, 10);

            GameplayEffectDef aura = new GameplayEffectDef
            {
                Name = "战鼓光环",
                Duration = EffectDurationKind.Permanent,
                Modifiers = new[] { new AttributeModifierDef
                {
                    Attribute = AttributeId.Attack, Op = ModOp.Add, Magnitude = ScopedValue.Const(1),
                } },
            };
            host.Engine.Execute(
                new[] { Group(TargetMode.AllAlliedUnits, new ApplyEffectOp { Effect = aura }) },
                SkillContext.Of(leader));

            Assert.AreEqual(4, u1.Attributes.Current(AttributeId.Attack));
            Assert.AreEqual(5, u2.Attributes.Current(AttributeId.Attack));

            host.Engine.RemoveEffectsFromSource(leader);
            Assert.AreEqual(3, u1.Attributes.Current(AttributeId.Attack), "光环随来源撤除失效");
            Assert.AreEqual(4, u2.Attributes.Current(AttributeId.Attack));
        }

        // ==================== 触发 ====================

        [Test]
        public void 触发_父标签订阅捕获子事件()
        {
            TestHost host = new TestHost();
            SkillEntity victim = host.AddUnit(TestHost.TeamB, "受击者", 1, 10);
            host.Engine.RegisterTrigger(victim,
                Trig("Event.Damage", Group(TargetMode.Self, new LogOp { Message = "hit" })));

            SkillEntity attacker = host.AddUnit(TestHost.TeamA, "攻击者", 1, 10);
            host.Engine.ResolveDamage(attacker, victim, 2);

            Assert.IsTrue(host.Log.Contains("hit"), "订阅 Event.Damage 父标签命中 Event.Damage.Applied 子事件");
        }

        [Test]
        public void 触发_Preview事前改写_减伤与免疫()
        {
            TestHost host = new TestHost();
            SkillEntity attacker = host.AddUnit(TestHost.TeamA, "攻击者", 1, 10);
            SkillEntity shielded = host.AddUnit(TestHost.TeamB, "披甲者", 1, 10);
            host.Engine.RegisterTrigger(shielded, Trig("Event.Damage.Preview",
                Group(TargetMode.Self, new ModifyEventMagnitudeOp { Delta = -1 }),
                TriggerTiming.Preview, selfOnly: true));

            host.Engine.ResolveDamage(attacker, shielded, 5);
            Assert.AreEqual(6, shielded.Attributes.Current(AttributeId.CurrentHp), "5-1 减伤");

            SkillEntity immune = host.AddUnit(TestHost.TeamB, "免疫者", 1, 10);
            host.Engine.RegisterTrigger(immune, Trig("Event.Damage.Preview",
                Group(TargetMode.Self, new ModifyEventMagnitudeOp { Scale = 0 }),
                TriggerTiming.Preview, selfOnly: true));
            host.Engine.ResolveDamage(attacker, immune, 5);
            Assert.AreEqual(10, immune.Attributes.Current(AttributeId.CurrentHp), "Scale=0 免疫");
        }

        [Test]
        public void 触发_EventValue反弹等量伤害()
        {
            TestHost host = new TestHost();
            SkillEntity attacker = host.AddUnit(TestHost.TeamA, "攻击者", 1, 10);
            SkillEntity spiky = host.AddUnit(TestHost.TeamB, "反伤甲", 1, 10);
            host.Engine.RegisterTrigger(spiky, Trig("Event.Damage.Applied",
                Group(TargetMode.Instigator, new DamageOp
                {
                    Amount = new ScopedValue { Kind = ValueSourceKind.EventValue },
                }), selfOnly: true));

            host.Engine.ResolveDamage(attacker, spiky, 3);
            Assert.AreEqual(7, spiky.Attributes.Current(AttributeId.CurrentHp));
            Assert.AreEqual(7, attacker.Attributes.Current(AttributeId.CurrentHp), "反弹等量 3 点");
        }

        [Test]
        public void 触发_SourceAttribute引用施法者身材并乘系数()
        {
            TestHost host = new TestHost();
            SkillEntity caster = host.AddUnit(TestHost.TeamA, "巨力法师", 4, 10);
            SkillEntity victim = host.AddUnit(TestHost.TeamB, "受害者", 1, 20);

            host.Engine.Execute(
                new[] { Group(TargetMode.Chosen, new DamageOp
                {
                    Amount = new ScopedValue
                    {
                        Kind = ValueSourceKind.SourceAttribute,
                        Attribute = AttributeId.Attack, Coefficient = 2.0,
                    },
                }) },
                new SkillContext { Source = caster, MainTarget = victim });

            Assert.AreEqual(12, victim.Attributes.Current(AttributeId.CurrentHp), "4×2=8 伤害");
        }

        [Test]
        public void 触发_SetByCaller传值()
        {
            TestHost host = new TestHost();
            SkillEntity caster = host.AddLeader(TestHost.TeamA, "领袖", 30);
            SkillEntity victim = host.AddUnit(TestHost.TeamB, "受害者", 1, 20);

            host.Engine.Execute(
                new[] { Group(TargetMode.Chosen, new DamageOp
                {
                    Amount = new ScopedValue { Kind = ValueSourceKind.SetByCaller, Key = "power" },
                }) },
                new SkillContext
                {
                    Source = caster, MainTarget = victim,
                    SetByCaller = new Dictionary<string, double> { ["power"] = 5 },
                });

            Assert.AreEqual(15, victim.Attributes.Current(AttributeId.CurrentHp));
        }

        [Test]
        public void 触发_注册顺序即触发顺序()
        {
            TestHost host = new TestHost();
            SkillTag go = TagRegistry.Declare("Test.Go");
            SkillEntity u = host.AddUnit(TestHost.TeamA, "单位", 1, 10);
            host.Engine.RegisterTrigger(u, Trig(go.Name, Group(TargetMode.Self, new LogOp { Message = "first" })));
            host.Engine.RegisterTrigger(u, Trig(go.Name, Group(TargetMode.Self, new LogOp { Message = "second" })));

            host.Engine.Raise(new GameplayEvent(go, null, u, 0, DamageSourceKind.None));
            CollectionAssert.AreEqual(new[] { "first", "second" }, host.Log, "注册顺序=触发顺序");
        }

        [Test]
        public void 触发_连锁深度防护_不无限不崩溃()
        {
            TestHost host = new TestHost();
            SkillEntity a = host.AddUnit(TestHost.TeamA, "镜像甲", 1, 1000000);
            SkillEntity b = host.AddUnit(TestHost.TeamB, "镜像乙", 1, 1000000);
            SkillTag reflect = SkillTags.EventDamageApplied;

            TriggerDef mirror(SkillEntity owner) => Trig(reflect.Name,
                Group(TargetMode.Instigator, new DamageOp
                {
                    Amount = new ScopedValue { Kind = ValueSourceKind.EventValue },
                }), selfOnly: true);
            host.Engine.RegisterTrigger(a, mirror(a));
            host.Engine.RegisterTrigger(b, mirror(b));

            Assert.DoesNotThrow(() => host.Engine.ResolveDamage(a, b, 1));
            Assert.Greater(host.Engine.DroppedDispatches, 0, "互相反伤的死链被深度防护截断");
        }

        [Test]
        public void 触发_Once每场一次()
        {
            TestHost host = new TestHost();
            SkillTag go = TagRegistry.Declare("Test.Once");
            SkillEntity u = host.AddUnit(TestHost.TeamA, "单位", 1, 10);
            host.Engine.RegisterTrigger(u, Trig(go.Name,
                Group(TargetMode.Self, new LogOp { Message = "boom" }), once: true));

            host.Engine.Raise(new GameplayEvent(go, null, u, 0, DamageSourceKind.None));
            host.Engine.Raise(new GameplayEvent(go, null, u, 0, DamageSourceKind.None));
            Assert.AreEqual(1, host.Log.Count, "只触发一次");
        }

        [Test]
        public void 触发_宿主回合事件成长与死亡观察闭环()
        {
            TestHost host = new TestHost();
            SkillEntity grower = host.AddUnit(TestHost.TeamA, "战意兽", 3, 5);
            host.Engine.RegisterTrigger(grower, Trig("Event.Turn.Started",
                Group(TargetMode.Self, StatAdd(AttributeId.Attack, 1))));

            host.BeginRound();
            host.BeginRound();
            host.BeginRound();
            Assert.AreEqual(6, grower.Attributes.Current(AttributeId.Attack), "每回合+1攻");

            // 死亡观察：打死 → 移出世界 + 广播 UnitDied（旁观者可见）
            SkillEntity observer = host.AddUnit(TestHost.TeamA, "旁观者", 1, 10);
            host.Engine.RegisterTrigger(observer, Trig(TestHost.EventUnitDied.Name,
                Group(TargetMode.Self, new LogOp { Message = "ally-saw-death" })));

            SkillEntity killer = host.AddUnit(TestHost.TeamB, "杀手", 9, 10);
            host.Engine.ResolveDamage(killer, grower, 99);

            Assert.GreaterOrEqual(host.Log.IndexOf("death"), 0, "死亡观察触发");
            Assert.IsNull(host.Find(grower.Id), "尸体移出世界");
            Assert.IsTrue(host.Log.Contains("ally-saw-death"), "UnitDied 广播被旁观者收到");
            Assert.Less(host.Log.IndexOf("death"), host.Log.IndexOf("ally-saw-death"),
                "先死亡观察、后旁观广播");
        }

        [Test]
        public void 触发_非触发上下文事件目标解析为空_不崩不作用()
        {
            TestHost host = new TestHost();
            SkillEntity u = host.AddUnit(TestHost.TeamA, "单位", 1, 10);
            SkillEntity victim = host.AddUnit(TestHost.TeamB, "受害者", 1, 10);

            Assert.DoesNotThrow(() => host.Engine.Execute(
                new[] { Group(TargetMode.Instigator, Dmg(5)) },
                new SkillContext { Source = u, MainTarget = victim }));
            Assert.AreEqual(10, victim.Attributes.Current(AttributeId.CurrentHp), "无事件上下文，Instigator 为空");
        }

        [Test]
        public void 触发_Preview配置违规抛异常()
        {
            TestHost host = new TestHost();
            SkillEntity u = host.AddUnit(TestHost.TeamA, "单位", 1, 10);

            // Preview 时机只能订阅 *.Preview 标签
            Assert.Throws<InvalidOperationException>(() => host.Engine.RegisterTrigger(u,
                Trig("Event.Damage.Applied", Group(TargetMode.Self, Dmg(1)), TriggerTiming.Preview)));

            // 改写 op 只在 Preview 上下文合法
            Assert.Throws<InvalidOperationException>(() => host.Engine.Execute(
                new[] { Group(TargetMode.Self, new ModifyEventMagnitudeOp { Delta = -1 }) },
                SkillContext.Of(u)));
        }

        // ==================== 引擎契约 ====================

        [Test]
        public void 确定性_同种子同操作同终局()
        {
            List<int> RunSeeded()
            {
                TestHost host = new TestHost(42);
                SkillEntity shooter = host.AddUnit(TestHost.TeamA, "射手", 1, 10);
                List<SkillEntity> foes = new List<SkillEntity>
                {
                    host.AddUnit(TestHost.TeamB, "敌一", 1, 100),
                    host.AddUnit(TestHost.TeamB, "敌二", 1, 100),
                    host.AddUnit(TestHost.TeamB, "敌三", 1, 100),
                };
                List<int> hps = new List<int>();
                for (int i = 0; i < 6; i++)
                {
                    host.Engine.Execute(
                        new[] { Group(TargetMode.RandomOpposingUnit, Dmg(1)) },
                        SkillContext.Of(shooter));
                    foes.ForEach(f => hps.Add((int)f.Attributes.Current(AttributeId.CurrentHp)));
                }
                return hps;
            }

            CollectionAssert.AreEqual(RunSeeded(), RunSeeded(), "同种子同操作序列=同血量序列");
        }

        [Test]
        public void 宿主自定义op注册与未知op报错()
        {
            TestHost host = new TestHost();
            SkillEntity u = host.AddUnit(TestHost.TeamA, "单位", 1, 10);

            host.Engine.Execute( // LogOp 是测试宿主注册的自定义 op
                new[] { Group(TargetMode.Self, new LogOp { Message = "custom" }) },
                SkillContext.Of(u));
            CollectionAssert.AreEqual(new[] { "custom" }, host.Log);

            Assert.Throws<InvalidOperationException>(() => host.Engine.Execute(
                new[] { Group(TargetMode.Self, new UnknownOp()) },
                SkillContext.Of(u)));
        }

        sealed class UnknownOp : EffectOp { }
    }
}
