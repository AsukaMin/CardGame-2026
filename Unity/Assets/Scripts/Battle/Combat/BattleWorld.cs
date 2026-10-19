using System.Collections.Generic;

namespace Battle
{
    /// <summary>战斗宿主的语义事件标签（技能系统设计§3.3：宿主声明+喂养，引擎不知其含义）。</summary>
    public static class BattleEventTags
    {
        public static readonly SkillTag TurnStarted = TagRegistry.Declare("Event.Turn.Started");
        public static readonly SkillTag UnitSummoned = TagRegistry.Declare("Event.Unit.Summoned");
        public static readonly SkillTag UnitDied = TagRegistry.Declare("Event.Unit.Died");
        public static readonly SkillTag CardDrawn = TagRegistry.Declare("Event.Card.Drawn");
    }

    /// <summary>种子随机源（确定性铁律：同种子同操作=同终局）。</summary>
    public sealed class SeededRandomSource : IRandomSource
    {
        readonly System.Random rnd;

        public SeededRandomSource(int seed) => rnd = new System.Random(seed);

        public int Range(int minInclusive, int maxExclusive) => rnd.Next(minInclusive, maxExclusive);
        public T Pick<T>(IReadOnlyList<T> items) => items[rnd.Next(items.Count)];
    }

    /// <summary>
    /// 战斗世界：ISkillWorld 的战斗宿主实现（技能系统经它看世界）。
    /// 实体表保持入场序（返回列表顺序稳定——确定性依赖它）；Id 会话内自增。
    /// </summary>
    public sealed class BattleWorld : ISkillWorld
    {
        public const int PlayerTeam = 1;
        public const int EnemyTeam = 2;

        readonly List<SkillEntity> entities = new List<SkillEntity>();
        readonly Dictionary<int, SkillEntity> byId = new Dictionary<int, SkillEntity>();
        int nextId = 1;

        public BattleWorld(IRandomSource random) => Random = random;

        /// <summary>效果伤害记录（RecordEffectDamageOp 写入，session 每次结算后排空广播）。</summary>
        public readonly List<(SkillEntity Source, SkillEntity Target, double Amount)> EffectDamageLog =
            new List<(SkillEntity, SkillEntity, double)>();

        public SkillEntity AddLeader(int team, string name, int maxHp) =>
            Add(team, name, EntityKind.Leader, 0, maxHp);

        public SkillEntity AddUnit(int team, string name, int attack, int maxHp) =>
            Add(team, name, EntityKind.Unit, attack, maxHp);

        SkillEntity Add(int team, string name, EntityKind kind, int attack, int maxHp)
        {
            SkillEntity e = new SkillEntity { Id = nextId++, TeamId = team, Kind = kind, Name = name };
            e.Attributes.SetBase(AttributeId.Attack, attack);
            e.Attributes.SetBase(AttributeId.MaxHp, maxHp);
            e.Attributes.SetBase(AttributeId.CurrentHp, maxHp);
            entities.Add(e);
            byId[e.Id] = e;
            return e;
        }

        /// <summary>移出世界（死亡离场）。效果撤除/触发器注销/死亡事件由调用方（session）经引擎完成。</summary>
        public void Remove(SkillEntity e)
        {
            entities.Remove(e);
            byId.Remove(e.Id);
        }

        // ---------- ISkillWorld ----------

        public IRandomSource Random { get; }

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

        public int OpposingTeamOf(int teamId) => teamId == PlayerTeam ? EnemyTeam : PlayerTeam;

        public SkillEntity Find(int entityId) => byId.TryGetValue(entityId, out SkillEntity e) ? e : null;
    }
}
