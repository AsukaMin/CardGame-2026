using System;
using System.Collections.Generic;

namespace Battle.Tests
{
    /// <summary>示例卡池：覆盖各槽型部位、公式变体、充能档位与条件效果（测试数据形状 = 未来导表 JSON 的形状）。</summary>
    public static class SampleData
    {
        // ---------- 部位卡 ----------

        /// <summary>利爪：身材 3/2，2 费。</summary>
        public static PartCardConfig Claw() => new PartCardConfig
        {
            Id = 101, Name = "利爪", SlotType = SlotType.Body, Cost = 2, Attack = 3, MaxHp = 2,
        };

        /// <summary>甲壳：身材 1/4，1 费。</summary>
        public static PartCardConfig Shell() => new PartCardConfig
        {
            Id = 102, Name = "甲壳", SlotType = SlotType.Body, Cost = 1, Attack = 1, MaxHp = 4,
        };

        /// <summary>心脏：身材 2/3，2 费。</summary>
        public static PartCardConfig Heart() => new PartCardConfig
        {
            Id = 103, Name = "心脏", SlotType = SlotType.Body, Cost = 2, Attack = 2, MaxHp = 3,
        };

        /// <summary>嘲讽甲壳：身材 0/5，2 费，附带嘲讽关键词。</summary>
        public static PartCardConfig TauntCarapace() => new PartCardConfig
        {
            Id = 104, Name = "嘲讽甲壳", SlotType = SlotType.Body, Cost = 2, Attack = 0, MaxHp = 5,
            GrantKeywords = Keywords.Taunt,
        };

        /// <summary>毒腺：入场——对随机敌方单位造成 2 伤害，2 费。</summary>
        public static PartCardConfig VenomGland() => new PartCardConfig
        {
            Id = 111, Name = "毒腺", SlotType = SlotType.OnSummon, Cost = 2,
            Effects = new[]
            {
                new EffectDef
                {
                    Target = TargetMode.RandomEnemyUnit,
                    Ops = new EffectOp[] { new DamageOp { Amount = 2 } },
                },
            },
        };

        /// <summary>战鼓：常驻——我方全体单位攻击 +1，3 费。</summary>
        public static PartCardConfig WarDrum() => new PartCardConfig
        {
            Id = 112, Name = "战鼓", SlotType = SlotType.Continuous, Cost = 3,
            Effects = new[]
            {
                new EffectDef
                {
                    Target = TargetMode.AllFriendlyUnits,
                    Ops = new EffectOp[]
                    {
                        new ModifyStatOp { Stat = StatType.Attack, Amount = 1, Duration = ModifierDurationKind.Battle },
                    },
                },
            },
        };

        /// <summary>怨念：离场——对敌方主战者造成 3 伤害，1 费，消耗（死亡即销毁不回收）。</summary>
        public static PartCardConfig Grudge() => new PartCardConfig
        {
            Id = 113, Name = "怨念", SlotType = SlotType.OnLeave, Cost = 1, Consumable = true,
            Effects = new[]
            {
                new EffectDef
                {
                    Target = TargetMode.EnemyHero,
                    Ops = new EffectOp[] { new DamageOp { Amount = 3 } },
                },
            },
        };

        // ---------- 怪兽卡套 ----------

        /// <summary>卡套工厂：布局 + 消耗公式 + 身材系数。</summary>
        public static MonsterCardConfig Sleeve(
            int id, string name, SlotType[] slots, double costMult = 1.0, int costFlat = 0, double statMult = 1.0) =>
            new MonsterCardConfig
            {
                Id = id,
                Name = name,
                Slots = slots,
                Cost = new CostFormula { Multiplier = costMult, Flat = costFlat },
                StatMultiplier = statMult,
            };

        /// <summary>斗士型 [身][入][离]，无修正。</summary>
        public static MonsterCardConfig FighterSleeve() =>
            Sleeve(201, "石壳斗士", new[] { SlotType.Body, SlotType.OnSummon, SlotType.OnLeave });

        /// <summary>豪华型 [身][身][常]，身材 ×1.3、消耗 ×1.3。</summary>
        public static MonsterCardConfig LuxurySleeve() =>
            Sleeve(202, "豪华熔核体", new[] { SlotType.Body, SlotType.Body, SlotType.Continuous },
                costMult: 1.3, statMult: 1.3);

        /// <summary>蛮兽型 [身][身]，无修正（敌方卡套池用）。</summary>
        public static MonsterCardConfig BruteSleeve() =>
            Sleeve(203, "蛮兽", new[] { SlotType.Body, SlotType.Body });

        /// <summary>预整备敌方蛮兽：利爪 + 甲壳。</summary>
        public static MonsterSleeve EnemyBruteSleeve() => Build(BruteSleeve(), Claw(), Shell());

        /// <summary>便捷装槽：按槽型找第一个空槽装入。</summary>
        public static MonsterSleeve Build(MonsterCardConfig config, params PartCardConfig[] parts)
        {
            MonsterSleeve sleeve = new MonsterSleeve(config);
            foreach (PartCardConfig part in parts)
            {
                bool placed = false;
                for (int i = 0; i < config.Slots.Length; i++)
                {
                    if (config.Slots[i] == part.SlotType && sleeve.Installed[i] == null)
                    {
                        sleeve.Installed[i] = part;
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                    throw new InvalidOperationException($"卡套[{config.Name}]无处可装部位[{part.Name}]（槽型 {part.SlotType}）");
            }
            return sleeve;
        }

        // ---------- 效果卡 ----------

        static EffectDef[] ChosenDamage(int amount) => new[]
        {
            new EffectDef { Target = TargetMode.Chosen, Ops = new EffectOp[] { new DamageOp { Amount = amount } } },
        };

        /// <summary>火球术：2 费低配 3 伤 / 4 费完全体 6 伤（指定目标）。</summary>
        public static EffectCardConfig Fireball() => new EffectCardConfig
        {
            Id = 301, Name = "火球术",
            Tiers = new[]
            {
                new EffectTier { Cost = 2, Label = "低配", Effects = ChosenDamage(3) },
                new EffectTier { Cost = 4, Label = "完全体", Effects = ChosenDamage(6) },
            },
        };

        /// <summary>治疗波：2 费治疗 4 / 4 费治疗 4 并抽 1。</summary>
        public static EffectCardConfig HealWave() => new EffectCardConfig
        {
            Id = 302, Name = "治疗波",
            Tiers = new[]
            {
                new EffectTier
                {
                    Cost = 2, Label = "低配",
                    Effects = new[]
                    {
                        new EffectDef { Target = TargetMode.Chosen, Ops = new EffectOp[] { new HealOp { Amount = 4 } } },
                    },
                },
                new EffectTier
                {
                    Cost = 4, Label = "完全体",
                    Effects = new[]
                    {
                        new EffectDef { Target = TargetMode.Chosen, Ops = new EffectOp[] { new HealOp { Amount = 4 } } },
                        new EffectDef { Target = TargetMode.FriendlyHero, Ops = new EffectOp[] { new DrawCardsOp { Count = 1 } } },
                    },
                },
            },
        };

        /// <summary>处决：3 费——目标半血以下 6 伤，否则 3 伤（条件效果样例）。</summary>
        public static EffectCardConfig Execute() => new EffectCardConfig
        {
            Id = 303, Name = "处决",
            Tiers = new[]
            {
                new EffectTier
                {
                    Cost = 3, Label = "唯一",
                    Effects = new[]
                    {
                        new EffectDef
                        {
                            Target = TargetMode.Chosen,
                            Ops = new EffectOp[]
                            {
                                new ConditionalOp
                                {
                                    Condition = new TargetHpBelowPercentCondition { Percent = 0.5 },
                                    Then = new EffectOp[] { new DamageOp { Amount = 6 } },
                                    Else = new EffectOp[] { new DamageOp { Amount = 3 } },
                                },
                            },
                        },
                    },
                },
            },
        };

        // ---------- 敌方 ----------

        public static EnemyConfig BasicEnemy() => new EnemyConfig
        {
            Id = 401, Name = "洞穴看门人", MaxHp = 20, AttackDamageMin = 3, AttackDamageMax = 5,
        };

        // ---------- 回复策略 ----------

        /// <summary>脚本化回复策略：验证框架钩子（记录调用并按配置发点）。</summary>
        public class ScriptedRecoveryPolicy : IRecoveryPolicy
        {
            public int TurnStartCalls, AttackCalls, DiscardCalls;
            public int PointsPerTurnStart, PointsPerAttack, PointsPerDiscard;

            public void OnTurnStart(RecoveryContext ctx)
            {
                TurnStartCalls++;
                if (PointsPerTurnStart > 0) ctx.Economy.Grant(PointsPerTurnStart);
            }

            public void OnFriendlyAttackCompleted(RecoveryContext ctx)
            {
                AttackCalls++;
                if (PointsPerAttack > 0) ctx.Economy.Grant(PointsPerAttack);
            }

            public void OnCardDiscarded(RecoveryContext ctx)
            {
                DiscardCalls++;
                if (PointsPerDiscard > 0) ctx.Economy.Grant(PointsPerDiscard);
            }
        }
    }
}
