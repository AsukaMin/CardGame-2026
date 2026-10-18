using System;
using System.Collections.Generic;

namespace Battle.Tests
{
    // ==================== 测试宿主自定义 op（演示扩展点 + 死亡观察动作） ====================

    /// <summary>把目标移出世界（死亡观察动作——战斗语义，宿主职责）。</summary>
    public sealed class RemoveEntityOp : EffectOp { }

    /// <summary>喂一个事件（宿主语义事件的 Raise 载体）。</summary>
    public sealed class RaiseEventOp : EffectOp
    {
        public string Tag = "";
        public double Magnitude;
    }

    /// <summary>向宿主日志写一行（顺序断言用）。</summary>
    public sealed class LogOp : EffectOp
    {
        public string Message = "";
    }

    /// <summary>测试宿主的 op 工厂。</summary>
    public sealed class HostOpSpec : OpSpec
    {
        public override void Execute(SkillEngine engine, SkillContext ctx)
        {
            TestHost host = (TestHost)engine.World;
            switch (Data)
            {
                case RemoveEntityOp _:
                    if (ctx.CurrentTarget != null) host.RemoveFromWorld(ctx.CurrentTarget);
                    break;
                case RaiseEventOp raise:
                    engine.Raise(new GameplayEvent(TagRegistry.Resolve(raise.Tag),
                        ctx.Event.Instigator, ctx.CurrentTarget, raise.Magnitude, DamageSourceKind.None));
                    break;
                case LogOp log:
                    host.Log.Add(log.Message);
                    break;
            }
        }
    }

    /// <summary>种子随机源（宿主注入示例）。</summary>
    public sealed class SeededRandom : IRandomSource
    {
        readonly Random rnd;
        public SeededRandom(int seed) => rnd = new Random(seed);
        public int Range(int minInclusive, int maxExclusive) => rnd.Next(minInclusive, maxExclusive);
        public T Pick<T>(IReadOnlyList<T> items) => items[rnd.Next(items.Count)];
    }

    /// <summary>
    /// 测试宿主：内存世界（两队+领袖+种子随机）+ 死亡观察。
    /// 死亡观察=世界级触发器（Event.Damage.Applied 且目标 CurrentHp≤0 → 移出世界并 Raise Event.Unit.Died）
    /// ——模拟未来战斗核心的职责，证明事件回流闭环；死亡判定本身不属于技能系统。
    /// </summary>
    public sealed class TestHost : ISkillWorld
    {
        public const int TeamA = 1;
        public const int TeamB = 2;

        static int nextId = 1;

        readonly List<SkillEntity> entities = new List<SkillEntity>();
        readonly Dictionary<int, SkillEntity> byId = new Dictionary<int, SkillEntity>();

        static TestHost()
        {
            EventTurnStarted = TagRegistry.Declare("Event.Turn.Started");
            EventUnitDied = TagRegistry.Declare("Event.Unit.Died");
        }

        public TestHost(int seed = 0)
        {
            Random = new SeededRandom(seed);
            Engine = new SkillEngine(this);
            Engine.RegisterOp<RemoveEntityOp>(op => new HostOpSpec { Data = op });
            Engine.RegisterOp<RaiseEventOp>(op => new HostOpSpec { Data = op });
            Engine.RegisterOp<LogOp>(op => new HostOpSpec { Data = op });
            RegisterDeathWatch();
        }

        public static SkillTag EventTurnStarted { get; private set; }
        public static SkillTag EventUnitDied { get; private set; }

        public SkillEngine Engine { get; }
        public IRandomSource Random { get; }
        /// <summary>事件动作日志（顺序断言用）。</summary>
        public List<string> Log { get; } = new List<string>();

        // ---------- 造实体 ----------

        public SkillEntity AddUnit(int team, string name, int attack, int maxHp) =>
            Add(team, name, EntityKind.Unit, attack, maxHp);

        public SkillEntity AddLeader(int team, string name, int maxHp) =>
            Add(team, name, EntityKind.Leader, 0, maxHp);

        SkillEntity Add(int team, string name, EntityKind kind, int attack, int maxHp)
        {
            SkillEntity e = new SkillEntity
            {
                Id = nextId++, TeamId = team, Kind = kind, Name = name,
            };
            e.Attributes.SetBase(AttributeId.Attack, attack);
            e.Attributes.SetBase(AttributeId.MaxHp, maxHp);
            e.Attributes.SetBase(AttributeId.CurrentHp, maxHp);
            entities.Add(e);
            byId[e.Id] = e;
            return e;
        }

        // ---------- 死亡观察（战斗语义——宿主职责的模拟） ----------

        void RegisterDeathWatch()
        {
            Engine.RegisterTrigger(null, new TriggerDef
            {
                EventTag = SkillTags.EventDamageApplied.Name,
                Filter = new AttributeCompareCondition
                {
                    Attribute = AttributeId.CurrentHp, Op = CompareOperator.LessOrEqual, Value = 0,
                },
                Effects = new EffectGroupDef
                {
                    Target = TargetMode.EventTarget,
                    Ops = new EffectOp[]
                    {
                        new LogOp { Message = "death" },
                        new RemoveEntityOp(),
                        new RaiseEventOp { Tag = EventUnitDied.Name },
                    },
                },
            });
        }

        /// <summary>移出世界 + 撤销其施加的效果（光环）+ 清其身上的效果 + 注销其触发器。</summary>
        public void RemoveFromWorld(SkillEntity e)
        {
            entities.Remove(e);
            byId.Remove(e.Id);
            Engine.RemoveEffectsFromSource(e);
            Engine.RemoveEntityEffects(e);
            Engine.UnregisterTriggers(e);
        }

        // ---------- 回合时点（宿主驱动的示例形状） ----------

        /// <summary>回合开始：喂回合事件 → 周期/轮数推进（顺序由宿主决定）。</summary>
        public void BeginRound()
        {
            Engine.Raise(new GameplayEvent(EventTurnStarted, null, null, 0, DamageSourceKind.None));
            Engine.TickRound();
        }

        /// <summary>回合结束：相位过期。</summary>
        public void EndRound() => Engine.ExpirePhase();

        // ---------- ISkillWorld ----------

        public IReadOnlyList<SkillEntity> AllEntities => entities;

        public IReadOnlyList<SkillEntity> EntitiesOfTeam(int teamId, EntityKind kind) =>
            entities.FindAll(e => e.TeamId == teamId && e.Kind == kind);

        public IReadOnlyList<SkillEntity> EntitiesNotOfTeam(int teamId, EntityKind kind) =>
            entities.FindAll(e => e.TeamId != teamId && e.Kind == kind);

        public SkillEntity LeaderOfTeam(int teamId)
        {
            for (int i = 0; i < entities.Count; i++)
                if (entities[i].TeamId == teamId && entities[i].Kind == EntityKind.Leader)
                    return entities[i];
            return null;
        }

        public int OpposingTeamOf(int teamId) => teamId == TeamA ? TeamB : TeamA;

        public SkillEntity Find(int entityId) =>
            byId.TryGetValue(entityId, out SkillEntity e) ? e : null;
    }
}
