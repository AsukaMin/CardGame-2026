using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>效果卡实例（手牌/牌库中的形态；打出即消失）。</summary>
    public class EffectCardInstance
    {
        public int Id;
        public EffectCardConfig Config;
    }

    /// <summary>牌库：只有效果卡——怪兽卡走战前整备名单，不入牌库（v2.1 规则）。</summary>
    public class Deck
    {
        readonly List<EffectCardInstance> cards = new List<EffectCardInstance>();

        public int Count => cards.Count;
        public IReadOnlyList<EffectCardInstance> Cards => cards;

        public void SetCards(IEnumerable<EffectCardInstance> instances)
        {
            cards.Clear();
            cards.AddRange(instances);
        }

        public void Shuffle(Rng rng) => rng.Shuffle(cards);

        /// <summary>抽顶牌；牌库空返回 null（无疲劳规则）。</summary>
        public EffectCardInstance DrawTop()
        {
            if (cards.Count == 0) return null;
            EffectCardInstance c = cards[cards.Count - 1];
            cards.RemoveAt(cards.Count - 1);
            return c;
        }
    }

    /// <summary>手牌（无上限）。</summary>
    public class Hand
    {
        readonly List<EffectCardInstance> cards = new List<EffectCardInstance>();

        public int Count => cards.Count;
        public IReadOnlyList<EffectCardInstance> Cards => cards;

        public void Add(EffectCardInstance card) => cards.Add(card);

        public EffectCardInstance GetAt(int index) =>
            index >= 0 && index < cards.Count ? cards[index] : null;

        public void Remove(EffectCardInstance card) => cards.Remove(card);
    }

    /// <summary>墓地：只收怪兽"整包"（卡套连同槽内部位卡）；效果卡打出即消失、无墓地循环。</summary>
    public class Graveyard
    {
        readonly List<MonsterSleeve> packages = new List<MonsterSleeve>();

        public int Count => packages.Count;
        public IReadOnlyList<MonsterSleeve> Packages => packages;

        public void Add(MonsterSleeve sleeve) => packages.Add(sleeve);
    }
}
