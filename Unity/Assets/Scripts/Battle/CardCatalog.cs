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

        static CardCatalog()
        {
            // 加载入口的完整前置（幂等）：宿主 op 判别可解析 + 内置标签已声明——
            // 标签声明在各静态类的字段初始化器里，不触碰类型就不执行，巡检会误报"未声明的标签"
            //（首张带标签的卡「剧毒」暴露的引导缺口）。
            SkillJson.TryRegisterOp<DrawCardsOp>("DrawCards");
            SkillJson.TryRegisterOp<AddCardToHandOp>("AddCardToHand");
            SkillTags.EnsureDeclared();
            BattleEventTags.EnsureDeclared();
        }

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
            // 二遍巡检（M5）：卡间引用的存在性——AddCardToHandOp（复制卡）与 EventSourceCardCondition（归因判定）
            // 的 CardId 必须指向本表存在的卡。巡检器只查标签不查卡号（宿主语义归加载入口），故在此补。
            foreach (BattleCard card in map.Values)
                CheckCardRefs(card, map);
            return new CardCatalog(map);
        }

        static void CheckCardRefs(BattleCard card, Dictionary<int, BattleCard> map)
        {
            if (card?.Effect == null) return;
            if (card.Effect.Effects != null)
                foreach (EffectGroupDef group in card.Effect.Effects)
                {
                    if (group == null) continue;
                    CheckOps(group.Ops, map, card.Name);
                }
            if (card.Effect.Triggers != null)
                foreach (TriggerDef trigger in card.Effect.Triggers)
                {
                    if (trigger == null) continue;
                    CheckCondition(trigger.Filter, map, card.Name);
                    if (trigger.Effects != null) CheckOps(trigger.Effects.Ops, map, card.Name);
                }
        }

        static void CheckOps(EffectOp[] ops, Dictionary<int, BattleCard> map, string cardName)
        {
            if (ops == null) return;
            foreach (EffectOp op in ops)
            {
                switch (op)
                {
                    case null: continue;
                    case AddCardToHandOp add:
                        RequireCard(map, add.CardId, $"「{cardName}」的 AddCardToHand");
                        break;
                    case ConditionalOp conditional:
                        CheckCondition(conditional.Condition, map, cardName);
                        CheckOps(conditional.Then, map, cardName);
                        CheckOps(conditional.Else, map, cardName);
                        break;
                }
            }
        }

        static void CheckCondition(ConditionDef condition, Dictionary<int, BattleCard> map, string cardName)
        {
            if (condition is EventSourceCardCondition source)
                RequireCard(map, source.CardId, $"「{cardName}」的 EventSourceCard 条件");
        }

        static void RequireCard(Dictionary<int, BattleCard> map, int cardId, string where)
        {
            if (!map.ContainsKey(cardId))
                throw new InvalidOperationException(
                    $"{where} 引用未定义的卡 id：{cardId}——先在 GameData/cards.json 配置（配置错误，加载期暴露）");
        }

        /// <summary>按 id 取卡（组牌用）；未定义即抛（配置错误，加载期暴露）。</summary>
        public BattleCard Get(int id) =>
            byId.TryGetValue(id, out BattleCard card)
                ? card
                : throw new InvalidOperationException($"卡 id 未定义：{id}——先在 GameData/cards.json 配置（配置错误）");

        public bool TryGet(int id, out BattleCard card) => byId.TryGetValue(id, out card);
    }
}
