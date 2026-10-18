using System;
using System.Collections.Generic;

namespace Fusion
{
    /// <summary>
    /// 怪兽卡套运行时实例：配置 + 已装入的部位卡。
    /// 装槽合法性（槽型匹配、≥1 身材槽部位卡）由整备阶段保证，这里只提供最小装入 API 与防御性校验（v2.1 定案）。
    /// </summary>
    public class MonsterSleeve
    {
        public MonsterCardConfig Config;
        /// <summary>与 Config.Slots 一一对应；null=空槽。</summary>
        public PartCardConfig[] Installed;

        public MonsterSleeve(MonsterCardConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Installed = new PartCardConfig[config.Slots.Length];
        }

        /// <summary>装入部位卡（整备期最小 API）：槽型必须匹配、槽必须为空、内容须符合槽型×内容矩阵。</summary>
        public void Install(int slotIndex, PartCardConfig part)
        {
            if (slotIndex < 0 || slotIndex >= Installed.Length)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            if (part == null) throw new ArgumentNullException(nameof(part));
            if (Installed[slotIndex] != null)
                throw new InvalidOperationException($"卡套[{Config.Name}]槽 {slotIndex} 已占用");
            SlotType slot = Config.Slots[slotIndex];
            if (part.SlotType != slot)
                throw new InvalidOperationException(
                    $"部位卡[{part.Name}]槽型 {part.SlotType} 与槽位类型 {slot} 不匹配");
            ValidateContentMatrix(slot, part);
            Installed[slotIndex] = part;
        }

        /// <summary>槽型×内容合法性矩阵（文档 §4）：身材槽纯身材；效果槽只带 Effects；常驻槽只带 Triggers/Auras。数据配置错误在装配期暴露。</summary>
        static void ValidateContentMatrix(SlotType slot, PartCardConfig part)
        {
            bool hasBody = part.Attack != 0 || part.MaxHp != 0;
            bool hasEffects = HasContent(part.Effects);
            bool hasContinuous = HasContent(part.Triggers) || HasContent(part.Auras);

            if (slot == SlotType.Body && (hasEffects || hasContinuous))
                throw new InvalidOperationException(
                    $"身材槽部位卡[{part.Name}]不得携带行为载荷（Effects/Triggers/Auras）");
            if (slot != SlotType.Body && hasBody)
                throw new InvalidOperationException(
                    $"效果槽/常驻槽部位卡[{part.Name}]不得携带身材数值（Attack/MaxHp）");
            if (slot == SlotType.Continuous && hasEffects)
                throw new InvalidOperationException(
                    $"常驻槽部位卡[{part.Name}]的行为应走 Triggers/Auras，不用 Effects（槽型不路由）");
            if ((slot == SlotType.OnSummon || slot == SlotType.OnLeave) && hasContinuous)
                throw new InvalidOperationException(
                    $"效果槽部位卡[{part.Name}]不得携带 Triggers/Auras（入场/离场由槽型路由）");
        }

        static bool HasContent<T>(T[] array) => array != null && array.Length > 0;

        /// <summary>防御性校验：至少一张身材槽部位卡（整备阶段应已保证）。</summary>
        public void Validate()
        {
            for (int i = 0; i < Installed.Length; i++)
                if (Config.Slots[i] == SlotType.Body && Installed[i] != null) return;
            throw new InvalidOperationException($"卡套[{Config.Name}]缺少身材槽部位卡——整备阶段应保证合法性");
        }

        /// <summary>槽内部位卡费用总和。</summary>
        public int TotalPartCost
        {
            get
            {
                int sum = 0;
                foreach (PartCardConfig p in Installed)
                    if (p != null) sum += p.Cost;
                return sum;
            }
        }

        /// <summary>公式召唤费：max(0, round(Σ费 × 乘数) + 定值)。先乘算取整、再加定值、下限 0。</summary>
        public int SummonCost
        {
            get
            {
                double scaled = TotalPartCost * Config.Cost.Multiplier;
                int rounded = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
                return Math.Max(0, rounded + Config.Cost.Flat);
            }
        }

        /// <summary>基础身材：Σ身材槽部位攻血（先求和）× 卡套身材系数。</summary>
        public (int attack, int maxHp) BaseStats
        {
            get
            {
                int atk = 0, hp = 0;
                for (int i = 0; i < Installed.Length; i++)
                {
                    if (Config.Slots[i] != SlotType.Body || Installed[i] == null) continue;
                    atk += Installed[i].Attack;
                    hp += Installed[i].MaxHp;
                }
                return (RoundStat(atk), RoundStat(hp));
            }
        }

        /// <summary>全部已装入部位卡。</summary>
        public IEnumerable<PartCardConfig> AllParts
        {
            get
            {
                foreach (PartCardConfig p in Installed)
                    if (p != null) yield return p;
            }
        }

        /// <summary>指定槽型的全部已装部位卡。</summary>
        public IEnumerable<PartCardConfig> PartsOf(SlotType slotType)
        {
            for (int i = 0; i < Installed.Length; i++)
                if (Config.Slots[i] == slotType && Installed[i] != null) yield return Installed[i];
        }

        int RoundStat(int v) => (int)Math.Round(v * Config.StatMultiplier, MidpointRounding.AwayFromZero);
    }
}
