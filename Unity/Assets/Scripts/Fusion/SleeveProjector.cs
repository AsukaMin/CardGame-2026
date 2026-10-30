using System;
using System.Collections.Generic;
using Battle;

namespace Fusion
{
    /// <summary>
    /// 卡套投影层（M5·T6）：装好的 MonsterSleeve → 战斗可消费的 BattleCard（SummonPayload 臂）。
    /// 《M2决定事项》要点 3"扁平卡是卡套的运行时投影，M5 由投影层替换"的落地——
    /// 放在 Fusion 程序集的原因：本域同时引用 Fusion 装配与 Battle 词汇（Battle 不反向依赖，方向铁律不破）。
    /// 数值面 = SummonCost / BaseStats（现成公式）；行为面 = 四路载荷聚合（顺序=槽位序→声明序，确定性）。
    /// </summary>
    public static class SleeveProjector
    {
        /// <summary>投影成 BattleCard（Id=卡套 id——归因可查；纯数据，无副作用）。</summary>
        public static BattleCard Compile(MonsterSleeve sleeve)
        {
            if (sleeve == null) throw new ArgumentNullException(nameof(sleeve));
            sleeve.Validate(); // 防御：缺身材槽部位卡拒绝投影（整备阶段应已保证，v2.1 定案）

            (int attack, int maxHp) = sleeve.BaseStats;
            SummonPayload payload = new SummonPayload
            {
                Name = sleeve.Config.Name,
                Cost = sleeve.SummonCost,
                Attack = attack,
                MaxHp = maxHp,
                OnSummonEffects = Collect(sleeve, SlotType.OnSummon, p => p.Effects),
                OnLeaveEffects = Collect(sleeve, SlotType.OnLeave, p => p.Effects),
                ContinuousTriggers = Collect(sleeve, SlotType.Continuous, p => p.Triggers),
                Auras = Collect(sleeve, SlotType.Continuous, p => p.Auras),
                GrantTags = CollectGrantTags(sleeve),
                Consumable = HasConsumable(sleeve),
            };
            return new BattleCard { Id = sleeve.Config.Id, Summon = payload };
        }

        static T[] Collect<T>(MonsterSleeve sleeve, SlotType slot, Func<PartCardConfig, T[]> pick)
        {
            List<T> list = null;
            foreach (PartCardConfig part in sleeve.PartsOf(slot))
            {
                T[] items = pick(part);
                if (items == null || items.Length == 0) continue;
                list ??= new List<T>();
                list.AddRange(items);
            }
            return list?.ToArray();
        }

        /// <summary>GrantTags 联合（矩阵允许任意槽位携带，Body 槽嘲讽甲壳是典型形态）。</summary>
        static string[] CollectGrantTags(MonsterSleeve sleeve)
        {
            List<string> list = null;
            foreach (PartCardConfig part in sleeve.AllParts)
            {
                if (part.GrantTags == null || part.GrantTags.Length == 0) continue;
                list ??= new List<string>();
                list.AddRange(part.GrantTags);
            }
            return list?.ToArray();
        }

        static bool HasConsumable(MonsterSleeve sleeve)
        {
            foreach (PartCardConfig part in sleeve.AllParts)
                if (part.Consumable) return true;
            return false;
        }
    }
}
