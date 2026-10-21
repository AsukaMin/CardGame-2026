using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>属性 Id（分段预留：1-99 战斗段=攻/MaxHp/CurrentHp/护盾，后续按需扩段）。</summary>
    public enum AttributeId
    {
        None = 0,
        Attack = 1,
        MaxHp = 2,
        CurrentHp = 3,
        Shield = 4,
    }

    /// <summary>修饰操作：聚合顺序固定为 Override ?? (Base+ΣAdd) × ΠMultiply。</summary>
    public enum ModOp { Add, Multiply, Override }

    /// <summary>
    /// 属性修饰条目：挂进属性的聚合器；Source=挂靠来源（ActiveEffectSpec），按来源精准撤销。
    /// StackCount 由所属效果推进——聚合时参与计算（Add×层数 / Multiply 线性叠乘），字段直读不反射。
    /// </summary>
    public class AttributeModifier
    {
        public AttributeId Attribute;
        public ModOp Op = ModOp.Add;
        public double Magnitude;
        /// <summary>挂靠来源（效果实例）——RemoveFromSource 精准撤销的钥匙。</summary>
        public object Source;
        public int StackCount = 1;
    }

    /// <summary>
    /// 单属性：Base/Current 双值（GAS 铁律）——Base 只被 Instant 直写；Current=聚合结果，脏标记重算。
    /// 【约定】CurrentHp 不挂修饰（伤害/治疗直改 Base，血上限修饰走 MaxHp）。
    /// </summary>
    public sealed class SkillAttribute
    {
        readonly List<AttributeModifier> modifiers = new List<AttributeModifier>();
        bool dirty = true;
        double current;

        public readonly AttributeId Id;
        public double BaseValue;

        public SkillAttribute(AttributeId id) => Id = id;

        public double CurrentValue
        {
            get { if (dirty) Recalculate(); return current; }
        }

        public void SetBase(double value)
        {
            BaseValue = value;
            dirty = true;
        }

        public void AddModifier(AttributeModifier modifier)
        {
            if (modifier == null) return;
            modifiers.Add(modifier);
            dirty = true;
        }

        public bool RemoveModifier(AttributeModifier modifier)
        {
            if (modifier == null || !modifiers.Remove(modifier)) return false;
            dirty = true;
            return true;
        }

        public int RemoveModifiersFromSource(object source)
        {
            int removed = modifiers.RemoveAll(m => ReferenceEquals(m.Source, source));
            if (removed > 0) dirty = true;
            return removed;
        }

        /// <summary>堆叠层数变化后标脏（重算时层数参与聚合）。</summary>
        public void MarkDirty() => dirty = true;

        /// <summary>是否仍挂有修饰（护盾抵扣账清零的判定用）。</summary>
        public bool HasModifiers => modifiers.Count > 0;

        void Recalculate()
        {
            dirty = false;
            double add = 0, mul = 1;
            bool hasOverride = false;
            double over = 0;
            for (int i = 0; i < modifiers.Count; i++)
            {
                AttributeModifier m = modifiers[i];
                switch (m.Op)
                {
                    case ModOp.Add:
                        add += m.Magnitude * m.StackCount;
                        break;
                    case ModOp.Multiply:
                        mul *= 1 + (m.Magnitude - 1) * m.StackCount; // 线性叠乘：+10%×3 层 = ×1.3
                        break;
                    case ModOp.Override:
                        over = m.Magnitude; // 多个 Override 时最后一个生效（配置应避免）
                        hasOverride = true;
                        break;
                }
            }
            current = hasOverride ? over : (BaseValue + add) * mul;
        }
    }

    /// <summary>
    /// 属性容器：字典 + MaxHp↔CurrentHp 联动（增益抬血/减益夹紧）+ 护盾抵扣账清零 + 变化事件。
    /// 事件在联动处理完之后触发（先血量落地，再广播）。
    /// </summary>
    public sealed class SkillAttributeContainer
    {
        readonly Dictionary<AttributeId, SkillAttribute> attrs =
            new Dictionary<AttributeId, SkillAttribute>();

        /// <summary>(属性, 变化前 Current, 变化后 Current)——CurrentValue 实际变化时触发。</summary>
        public event Action<AttributeId, double, double> OnAttributeChanged;

        public SkillAttribute Get(AttributeId id)
        {
            if (id == AttributeId.None)
                throw new InvalidOperationException("AttributeId.None 不是合法属性");
            if (!attrs.TryGetValue(id, out SkillAttribute a))
            {
                a = new SkillAttribute(id);
                attrs[id] = a;
            }
            return a;
        }

        public double Base(AttributeId id) => Get(id).BaseValue;
        public double Current(AttributeId id) => Get(id).CurrentValue;

        /// <summary>整数出口（宿主读面板值）：AwayFromZero 取整。</summary>
        public int CurrentInt(AttributeId id) =>
            (int)Math.Round(Current(id), MidpointRounding.AwayFromZero);

        public void SetBase(AttributeId id, double value)
        {
            SkillAttribute a = Get(id);
            double before = a.CurrentValue;
            a.SetBase(value);
            AfterChange(id, before);
        }

        public void AddModifier(AttributeModifier modifier)
        {
            SkillAttribute a = Get(modifier.Attribute);
            double before = a.CurrentValue;
            a.AddModifier(modifier);
            AfterChange(modifier.Attribute, before);
        }

        public void RemoveModifier(AttributeModifier modifier)
        {
            SkillAttribute a = Get(modifier.Attribute);
            double before = a.CurrentValue;
            a.RemoveModifier(modifier);
            AfterChange(modifier.Attribute, before);
        }

        /// <summary>撤销某来源挂的全部修饰（以效果实例为 Source 精准撤销）。</summary>
        public void RemoveModifiersFromSource(object source)
        {
            foreach (KeyValuePair<AttributeId, SkillAttribute> kv in attrs)
            {
                SkillAttribute a = kv.Value;
                double before = a.CurrentValue;
                if (a.RemoveModifiersFromSource(source) > 0)
                    AfterChange(kv.Key, before);
            }
        }

        /// <summary>某属性的修饰层数变化后重算并广播（堆叠推进用）。</summary>
        public void Touch(AttributeId id)
        {
            SkillAttribute a = Get(id);
            double before = a.CurrentValue;
            a.MarkDirty();
            AfterChange(id, before);
        }

        void AfterChange(AttributeId id, double before)
        {
            double after = Current(id);

            // MaxHp↔CurrentHp 联动：增益同步抬血、减益随后夹紧
            if (id == AttributeId.MaxHp && after != before)
            {
                SkillAttribute hp = Get(AttributeId.CurrentHp);
                double hpBefore = hp.CurrentValue;
                double desired = hp.BaseValue + (after - before > 0 ? after - before : 0);
                double clamped = Math.Max(0, Math.Min(desired, after));
                if (clamped != hp.BaseValue)
                {
                    hp.SetBase(clamped);
                    double hpAfter = hp.CurrentValue;
                    if (hpAfter != hpBefore)
                        OnAttributeChanged?.Invoke(AttributeId.CurrentHp, hpBefore, hpAfter);
                }
            }

            // CurrentHp 永远夹在 [0, MaxHp.Current]
            if (id == AttributeId.CurrentHp)
            {
                SkillAttribute hp = Get(id);
                double max = Current(AttributeId.MaxHp);
                double clamped = Math.Max(0, Math.Min(hp.BaseValue, max));
                if (clamped != hp.BaseValue)
                {
                    hp.SetBase(clamped);
                    after = hp.CurrentValue;
                }
            }

            // 盾耗尽后的抵扣账清零：盾消耗记账在 Base 负值（Current=Base+Σ修饰 ≥ 0 恒成立），
            // 修饰（临时盾）全部摘除后清账——新盾不被旧账吃（防御卡管线规则，与 MaxHp↔CurrentHp 联动同级）
            if (id == AttributeId.Shield)
            {
                SkillAttribute shield = Get(id);
                if (shield.BaseValue < 0 && !shield.HasModifiers)
                {
                    shield.SetBase(0);
                    after = shield.CurrentValue;
                }
            }

            if (after != before)
                OnAttributeChanged?.Invoke(id, before, after);
        }
    }
}
