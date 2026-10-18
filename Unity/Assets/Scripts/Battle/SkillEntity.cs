using System.Collections.Generic;

namespace Battle
{
    /// <summary>实体种类：单位 / 领袖（主战者——无攻击底数，引用 Attack 自然得 0）。</summary>
    public enum EntityKind { Unit, Leader }

    /// <summary>
    /// 技能实体（ASC 形态）：属性/标签/持续效果三容器 + 阵营与种类。
    /// 宿主持有并驱动；实体侧事件经容器暴露（OnAttributeChanged / OnTagAdded/Removed）。
    /// </summary>
    public sealed class SkillEntity
    {
        public int Id;
        public int TeamId;
        public EntityKind Kind = EntityKind.Unit;
        public string Name = "";

        public readonly SkillAttributeContainer Attributes = new SkillAttributeContainer();
        public readonly TagContainer Tags = new TagContainer();
        public readonly ActiveEffectContainer Effects;

        public SkillEntity() => Effects = new ActiveEffectContainer(this);

        public bool HasTag(SkillTag tag) => Tags.Has(tag);
        public bool HasTag(string tagName) => Tags.Has(TagRegistry.Resolve(tagName));

        public override string ToString() => $"[{Name}#{Id}]";
    }

    /// <summary>确定性随机源（宿主注入种子实现——技能系统的目标随机全部经它）。</summary>
    public interface IRandomSource
    {
        int Range(int minInclusive, int maxExclusive);
        T Pick<T>(IReadOnlyList<T> items);
    }

    /// <summary>
    /// 宿主实现的"眼睛"：技能系统经它看世界。阵营抽象=TeamId（int，无战斗词）。
    /// 返回列表的顺序由宿主保证稳定（确定性依赖它）。
    /// </summary>
    public interface ISkillWorld
    {
        /// <summary>世界中的全部实体（含双方领袖与单位）——时点推进/来源撤除遍历用。</summary>
        IReadOnlyList<SkillEntity> AllEntities { get; }

        IReadOnlyList<SkillEntity> EntitiesOfTeam(int teamId, EntityKind kind);
        IReadOnlyList<SkillEntity> EntitiesNotOfTeam(int teamId, EntityKind kind);
        SkillEntity LeaderOfTeam(int teamId);
        /// <summary>相对阵营 Id（双阵营对战：对方 team；宿主定义映射）。</summary>
        int OpposingTeamOf(int teamId);
        SkillEntity Find(int entityId);
        IRandomSource Random { get; }
    }
}
