using System;
using System.Collections.Generic;

namespace Battle
{
    // ==================== POCO（GameData/decks.json 与 enemies.json 的形状） ====================

    /// <summary>牌组条目：卡 id × 张数（count 省略=1）；牌库顺序 = 条目序展开（抽牌顺序敏感，数据保序）。</summary>
    [Serializable]
    public sealed class DeckEntry
    {
        public int Id;
        public int Count = 1;
    }

    /// <summary>牌组定义（decks.json 的一个元素）。牌组构筑 UI 归 M6；M5 只有初始牌组数据。</summary>
    [Serializable]
    public sealed class DeckDef
    {
        public int Id;
        public string Name = "";
        public DeckEntry[] Cards = Array.Empty<DeckEntry>();
    }

    /// <summary>敌方召唤条目：怪兽卡 id × 张数（敌方回合依序召唤一只，耗尽跳过）。</summary>
    [Serializable]
    public sealed class EnemySummonEntry
    {
        public int Id;
        public int Count = 1;
    }

    /// <summary>敌人定义（enemies.json 的一个元素）：领袖 HP + 召唤序列——差异只做数据面（行为差异归 M6 意图系统）。
    /// 它是 M6 levels.json 的前身，形状往那边靠（id/name/阵容）。</summary>
    [Serializable]
    public sealed class EnemyDef
    {
        public int Id;
        public string Name = "";
        public int LeaderHp = 30;
        public EnemySummonEntry[] Summons = Array.Empty<EnemySummonEntry>();
    }

    // ==================== 加载入口（CardCatalog 同款：零 IO、Load(string)+巡检、byId） ====================

    /// <summary>牌组仓库（M5·T3）：吃 decks.json 文本，巡检卡 id 引用存在性后吐按 id 可查的牌组集合。</summary>
    public sealed class DeckCatalog
    {
        readonly Dictionary<int, DeckDef> byId;
        readonly List<DeckDef> ordered;

        DeckCatalog(Dictionary<int, DeckDef> byId, List<DeckDef> ordered)
        {
            this.byId = byId;
            this.ordered = ordered;
        }

        public int Count => ordered.Count;
        public IReadOnlyList<DeckDef> All => ordered;

        /// <summary>第一个牌组（M5 初始牌组——牌组选择 UI 归 M6）。</summary>
        public DeckDef First => ordered.Count > 0
            ? ordered[0]
            : throw new InvalidOperationException("decks.json 为空——至少配置一个牌组（配置错误，加载期暴露）");

        public DeckDef Get(int id) =>
            byId.TryGetValue(id, out DeckDef deck)
                ? deck
                : throw new InvalidOperationException($"牌组 id 未定义：{id}——先在 GameData/decks.json 配置（配置错误）");

        public static DeckCatalog Load(string json, CardCatalog cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            DeckDef[] decks = SkillJson.Deserialize<DeckDef[]>(json) ?? Array.Empty<DeckDef>();
            Dictionary<int, DeckDef> map = new Dictionary<int, DeckDef>();
            List<DeckDef> ordered = new List<DeckDef>();
            foreach (DeckDef deck in decks)
            {
                if (deck == null || deck.Id <= 0)
                    throw new InvalidOperationException("牌组 id 非法（须为正整数）——配置错误，加载期暴露");
                if (map.ContainsKey(deck.Id))
                    throw new InvalidOperationException($"牌组 id 重复：{deck.Id}——配置错误，加载期暴露");
                if (deck.Cards == null || deck.Cards.Length == 0)
                    throw new InvalidOperationException($"牌组 {deck.Id}（{deck.Name}）cards 为空——配置错误，加载期暴露");
                foreach (DeckEntry entry in deck.Cards)
                {
                    if (entry == null || !cards.TryGet(entry.Id, out _))
                        throw new InvalidOperationException(
                            $"牌组 {deck.Id}（{deck.Name}）引用未定义的卡 id：{entry?.Id}——先在 cards.json 配置（配置错误，加载期暴露）");
                    if (entry.Count < 1)
                        throw new InvalidOperationException(
                            $"牌组 {deck.Id}（{deck.Name}）卡 {entry.Id} 数量非法：{entry.Count}（配置错误，加载期暴露）");
                }
                map.Add(deck.Id, deck);
                ordered.Add(deck);
            }
            return new DeckCatalog(map, ordered);
        }
    }

    /// <summary>敌人仓库（M5·T3）：吃 enemies.json 文本，巡检召唤引用（须为怪兽卡）后吐按 id 可查的敌人集合。</summary>
    public sealed class EnemyCatalog
    {
        readonly Dictionary<int, EnemyDef> byId;
        readonly List<EnemyDef> ordered;

        EnemyCatalog(Dictionary<int, EnemyDef> byId, List<EnemyDef> ordered)
        {
            this.byId = byId;
            this.ordered = ordered;
        }

        public int Count => ordered.Count;
        public IReadOnlyList<EnemyDef> All => ordered;

        public EnemyDef Get(int id) =>
            byId.TryGetValue(id, out EnemyDef enemy)
                ? enemy
                : throw new InvalidOperationException($"敌人 id 未定义：{id}——先在 GameData/enemies.json 配置（配置错误）");

        public static EnemyCatalog Load(string json, CardCatalog cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            EnemyDef[] enemies = SkillJson.Deserialize<EnemyDef[]>(json) ?? Array.Empty<EnemyDef>();
            Dictionary<int, EnemyDef> map = new Dictionary<int, EnemyDef>();
            List<EnemyDef> ordered = new List<EnemyDef>();
            foreach (EnemyDef enemy in enemies)
            {
                if (enemy == null || enemy.Id <= 0)
                    throw new InvalidOperationException("敌人 id 非法（须为正整数）——配置错误，加载期暴露");
                if (map.ContainsKey(enemy.Id))
                    throw new InvalidOperationException($"敌人 id 重复：{enemy.Id}——配置错误，加载期暴露");
                if (enemy.LeaderHp <= 0)
                    throw new InvalidOperationException($"敌人 {enemy.Id}（{enemy.Name}）leaderHp 必须为正——配置错误，加载期暴露");
                foreach (EnemySummonEntry entry in enemy.Summons)
                {
                    if (entry == null || !cards.TryGet(entry.Id, out BattleCard card) || !card.IsMonster)
                        throw new InvalidOperationException(
                            $"敌人 {enemy.Id}（{enemy.Name}）召唤引用非法：{entry?.Id}（须为 cards.json 已定义的怪兽卡）——配置错误，加载期暴露");
                    if (entry.Count < 1)
                        throw new InvalidOperationException(
                            $"敌人 {enemy.Id}（{enemy.Name}）召唤 {entry.Id} 数量非法：{entry.Count}（配置错误，加载期暴露）");
                }
                map.Add(enemy.Id, enemy);
                ordered.Add(enemy);
            }
            return new EnemyCatalog(map, ordered);
        }
    }
}
