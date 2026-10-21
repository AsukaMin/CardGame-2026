using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 卡牌仓库（M2 加载入口）：吃 cards.json 文本，吐按 id 可查的卡集合。
    /// 零 IO——读文件在调用方（headless 测试 / EditMode / 编辑器菜单，目录纪律）；
    /// 加载期校验 = SkillJson.Deserialize 内置巡检（标签/判别/单卡形状）+ 本类 id 唯一性巡检，不带病运行。
    /// </summary>
    public sealed class CardCatalog
    {
        readonly Dictionary<int, BattleCard> byId;

        static CardCatalog() =>
            // 内置表含抽牌卡（DrawCardsOp）——加载入口保证其判别可解析；幂等，BattleSession 构造时也会登记
            SkillJson.TryRegisterOp<DrawCardsOp>("DrawCards");

        CardCatalog(Dictionary<int, BattleCard> byId) => this.byId = byId;

        public int Count => byId.Count;

        /// <summary>全表卡集合（顺序无承诺，界面列举用）。</summary>
        public IEnumerable<BattleCard> Entries => byId.Values;

        /// <summary>解析卡表文本（全量校验，配置错误加载期暴露）。</summary>
        public static CardCatalog Load(string json)
        {
            CardEntry[] entries = SkillJson.Deserialize<CardEntry[]>(json);
            Dictionary<int, BattleCard> map = new Dictionary<int, BattleCard>();
            if (entries == null) return new CardCatalog(map);
            foreach (CardEntry entry in entries)
            {
                if (map.TryGetValue(entry.Id, out BattleCard existing))
                    throw new InvalidOperationException(
                        $"卡表 id 重复：{entry.Id}（「{entry.Card.Name}」与「{existing.Name}」撞号）——配置错误，加载期暴露");
                map.Add(entry.Id, entry.Card);
            }
            return new CardCatalog(map);
        }

        /// <summary>按 id 取卡（组牌用）；未定义即抛（配置错误，加载期暴露）。</summary>
        public BattleCard Get(int id) =>
            byId.TryGetValue(id, out BattleCard card)
                ? card
                : throw new InvalidOperationException($"卡 id 未定义：{id}——先在 GameData/cards.json 配置（配置错误）");

        public bool TryGet(int id, out BattleCard card) => byId.TryGetValue(id, out card);
    }
}
