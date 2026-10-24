using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 层级标签（GAS GameplayTag 的确定性实现）：
    /// int Id（注册表声明序，不用 string.GetHashCode——跨运行不稳定）+ 全名 + 祖先链。
    /// 层级匹配：拥有判定 = 自身或任一祖先命中（"Event.Damage.Applied" 拥有 "Event.Damage"）。
    /// Id 只做运行期内等值比较，跨运行不持久化；配置侧一律用字符串全名。
    /// </summary>
    public readonly struct SkillTag : IEquatable<SkillTag>
    {
        public readonly int Id;
        public readonly string Name;
        /// <summary>祖先链（从根到父，不含自身）。</summary>
        public readonly SkillTag[] Ancestors;

        internal SkillTag(int id, string name, SkillTag[] ancestors)
        {
            Id = id;
            Name = name;
            Ancestors = ancestors ?? Array.Empty<SkillTag>();
        }

        public bool IsValid => Id != 0;
        public bool IsEmpty => Id == 0;
        public int Depth => Ancestors.Length;

        /// <summary>拥有判定（层级匹配）：other 为自身或任一祖先时为真——父标签拥有全部子孙。</summary>
        public bool Matches(SkillTag other)
        {
            if (!IsValid || !other.IsValid) return false;
            if (Id == other.Id) return true;
            SkillTag[] a = Ancestors;
            for (int i = 0; i < a.Length; i++)
                if (a[i].Id == other.Id) return true;
            return false;
        }

        public bool MatchesExact(SkillTag other) => IsValid && other.IsValid && Id == other.Id;

        public bool Equals(SkillTag other) => Id == other.Id;
        public override bool Equals(object obj) => obj is SkillTag t && Id == t.Id;
        public override int GetHashCode() => Id;
        public static bool operator ==(SkillTag l, SkillTag r) => l.Id == r.Id;
        public static bool operator !=(SkillTag l, SkillTag r) => l.Id != r.Id;
        public override string ToString() => Name ?? "None";
    }

    /// <summary>
    /// 标签注册表：声明序=Id 序（确定性铁律）。声明即注册全部祖先；同名幂等。
    /// 未声明的名字 Resolve 时抛异常——配置错误在加载期暴露，不带病运行。
    /// </summary>
    public static class TagRegistry
    {
        static readonly Dictionary<string, SkillTag> byName = new Dictionary<string, SkillTag>();
        static int nextId = 1;

        public static SkillTag Declare(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new InvalidOperationException("标签名不能为空");
            if (byName.TryGetValue(name, out SkillTag existing)) return existing;

            int lastDot = name.LastIndexOf('.');
            SkillTag[] ancestors;
            if (lastDot <= 0)
            {
                ancestors = Array.Empty<SkillTag>();
            }
            else
            {
                SkillTag parent = Declare(name.Substring(0, lastDot));
                ancestors = new SkillTag[parent.Ancestors.Length + 1];
                Array.Copy(parent.Ancestors, ancestors, parent.Ancestors.Length);
                ancestors[parent.Ancestors.Length] = parent;
            }

            SkillTag tag = new SkillTag(nextId++, name, ancestors);
            byName[name] = tag;
            return tag;
        }

        public static SkillTag Resolve(string name)
        {
            if (!string.IsNullOrEmpty(name) && byName.TryGetValue(name, out SkillTag tag)) return tag;
            throw new InvalidOperationException($"未声明的标签：\"{name}\"——先在 SkillTags 或宿主静态类里 Declare（配置错误，加载期暴露）");
        }

        public static bool TryResolve(string name, out SkillTag tag)
        {
            if (!string.IsNullOrEmpty(name) && byName.TryGetValue(name, out tag)) return true;
            tag = default;
            return false;
        }

        public static IEnumerable<SkillTag> All => byName.Values;
    }

    /// <summary>配置期静态标签集合（数据字段用 string[] 名称，加载/注册时经 TagSet.Parse 解析）。</summary>
    public sealed class TagSet
    {
        public readonly SkillTag[] Tags;

        public TagSet(params SkillTag[] tags) => Tags = tags ?? Array.Empty<SkillTag>();

        public static TagSet Parse(params string[] names)
        {
            if (names == null || names.Length == 0) return new TagSet();
            SkillTag[] tags = new SkillTag[names.Length];
            for (int i = 0; i < names.Length; i++) tags[i] = TagRegistry.Resolve(names[i]);
            return new TagSet(tags);
        }

        public bool IsEmpty => Tags.Length == 0;
    }

    /// <summary>
    /// 运行时可变标签容器（实体持有）：引用计数——同一标签多来源时，
    /// Add 计数 0→1 触发 OnTagAdded，Remove 计数 1→0 触发 OnTagRemoved，中间态只改计数。
    /// 解决"两个效果都给同一标签，一个到期不能摘标签"的正确性问题。
    /// </summary>
    public sealed class TagContainer
    {
        readonly Dictionary<int, int> counts = new Dictionary<int, int>();
        readonly Dictionary<int, SkillTag> active = new Dictionary<int, SkillTag>();

        public event Action<SkillTag> OnTagAdded;
        public event Action<SkillTag> OnTagRemoved;

        public void Add(SkillTag tag)
        {
            if (!tag.IsValid) return;
            counts.TryGetValue(tag.Id, out int c);
            counts[tag.Id] = c + 1;
            if (c == 0)
            {
                active[tag.Id] = tag;
                OnTagAdded?.Invoke(tag);
            }
        }

        public bool Remove(SkillTag tag)
        {
            if (!tag.IsValid) return false;
            if (!counts.TryGetValue(tag.Id, out int c) || c == 0) return false;
            if (c == 1)
            {
                counts.Remove(tag.Id);
                active.Remove(tag.Id);
                OnTagRemoved?.Invoke(tag);
            }
            else
            {
                counts[tag.Id] = c - 1;
            }
            return true;
        }

        /// <summary>层级匹配拥有判定：身上任一激活标签拥有 tag（tag 为其自身或祖先）。</summary>
        public bool Has(SkillTag tag)
        {
            if (!tag.IsValid) return false;
            if (active.ContainsKey(tag.Id)) return true;
            foreach (SkillTag t in active.Values)
                if (t.Matches(tag)) return true;
            return false;
        }

        public bool HasExact(SkillTag tag) => tag.IsValid && active.ContainsKey(tag.Id);

        public bool HasAny(TagSet set)
        {
            SkillTag[] tags = set.Tags;
            for (int i = 0; i < tags.Length; i++)
                if (Has(tags[i])) return true;
            return false;
        }

        public bool HasAll(TagSet set)
        {
            SkillTag[] tags = set.Tags;
            for (int i = 0; i < tags.Length; i++)
                if (!Has(tags[i])) return false;
            return true;
        }

        public IEnumerable<SkillTag> ActiveTags => active.Values;

        /// <summary>清空（实体销毁/测试用）——不触发事件，非摘除语义。</summary>
        public void Clear()
        {
            counts.Clear();
            active.Clear();
        }
    }

    /// <summary>项目标签声明（声明即文档；宿主的战斗语义标签由宿主自建静态类声明并喂养）。</summary>
    public static class SkillTags
    {
        // ---------- 行为关键词（载体；行为=宿主管线钩子或触发器数据） ----------
        public static readonly SkillTag KeywordTaunt = TagRegistry.Declare("Keyword.Taunt");
        public static readonly SkillTag KeywordPoison = TagRegistry.Declare("Keyword.Poison");
        public static readonly SkillTag KeywordLifesteal = TagRegistry.Declare("Keyword.Lifesteal");

        // ---------- 引擎自发事件（结算管线时点） ----------
        public static readonly SkillTag EventDamagePreview = TagRegistry.Declare("Event.Damage.Preview");
        public static readonly SkillTag EventDamageApplied = TagRegistry.Declare("Event.Damage.Applied");
        public static readonly SkillTag EventHealPreview = TagRegistry.Declare("Event.Heal.Preview");
        public static readonly SkillTag EventHealApplied = TagRegistry.Declare("Event.Heal.Applied");
        public static readonly SkillTag EventEffectApplied = TagRegistry.Declare("Event.Effect.Applied");
        public static readonly SkillTag EventEffectRemoved = TagRegistry.Declare("Event.Effect.Removed");

        /// <summary>事前时点标签名约定后缀（Timing.Preview 只允许订阅 *.Preview 标签）。</summary>
        public const string PreviewSuffix = ".Preview";

        /// <summary>强制触发本类的静态标签声明（加载入口在反序列化前调用——标签须先 Declare 再巡检）。
        /// 触碰静态字段保证类型初始化器执行（beforefieldinit 语义下仅调方法不保证）。</summary>
        public static void EnsureDeclared() => _ = KeywordTaunt;
    }
}
