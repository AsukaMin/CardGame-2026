using System;

namespace Battle
{
    /// <summary>
    /// 数据巡检（加载期校验）：反序列化完成后遍历 Data 树，把所有标签字符串位逐个 TagRegistry.Resolve。
    /// 标签是普通 string 字段、转换器无从分辨"标签串/普通串"，故按 Data 合同逐型编码行走；
    /// 报错带数据路径（如 root.effects[0].ops[1].tag），直指配置文件里的具体位。
    /// 宿主自定义 op 的字段不在巡检范围（编辑器时代的扩展点）；未知类型直接跳过。
    /// </summary>
    public static class SkillJsonValidator
    {
        /// <summary>巡检任意反序列化结果（SkillJson.Deserialize 内置调用，CardCatalog 等加载入口可复用）。</summary>
        public static void Validate(object root) => Visit(root, "root");

        static void Visit(object node, string path)
        {
            switch (node)
            {
                case null: return;
                case EffectOp op: VisitOp(op, path); return;
                case ConditionDef condition: VisitCondition(condition, path); return;
                case EffectGroupDef group: VisitGroup(group, path); return;
                case GameplayEffectDef effect: VisitGameplayEffect(effect, path); return;
                case TriggerDef trigger: VisitTrigger(trigger, path); return;
                case EffectCardDef card: VisitList(card.Effects, $"{path}.effects", VisitGroup); return;
                case MonsterCardDef _: return;
                case CardEntry card: VisitCardEntry(card, path); return;
                case BattleCard battleCard: VisitBattleCard(battleCard, path); return;
                case CardEntry[] entries: VisitList(entries, path, VisitCardEntry); return;
                case EffectOp[] ops: VisitList(ops, path, VisitOp); return;
                case ConditionDef[] conditions: VisitList(conditions, path, VisitCondition); return;
                case EffectGroupDef[] groups: VisitList(groups, path, VisitGroup); return;
                case GameplayEffectDef[] effects: VisitList(effects, path, VisitGameplayEffect); return;
                case TriggerDef[] triggers: VisitList(triggers, path, VisitTrigger); return;
            }
        }

        static void VisitOp(EffectOp op, string path)
        {
            switch (op)
            {
                case GrantTagOp grant:
                    ResolveTag(grant.Tag, $"{path}.tag");
                    break;
                case RemoveTagOp remove:
                    ResolveTag(remove.Tag, $"{path}.tag");
                    break;
                case ApplyEffectOp apply:
                    VisitGameplayEffect(apply.Effect, $"{path}.effect");
                    break;
                case ConditionalOp conditional:
                    VisitCondition(conditional.Condition, $"{path}.condition");
                    VisitList(conditional.Then, $"{path}.then", VisitOp);
                    VisitList(conditional.Else, $"{path}.else", VisitOp);
                    break;
                // Damage/Heal/ModifyAttribute/ModifyEventMagnitude 无标签位；宿主自定义 op 字段不在巡检范围。
            }
        }

        static void VisitCondition(ConditionDef condition, string path)
        {
            if (condition is HasTagCondition hasTag)
                ResolveTag(hasTag.Tag, $"{path}.tag");
        }

        static void VisitGroup(EffectGroupDef group, string path)
        {
            if (group == null) return;
            VisitList(group.Ops, $"{path}.ops", VisitOp);
            // Cues：表现数据位，无标签。
        }

        static void VisitGameplayEffect(GameplayEffectDef effect, string path)
        {
            if (effect == null) return;
            ResolveTags(effect.AssetTags, $"{path}.assetTags");
            ResolveTags(effect.GrantedTags, $"{path}.grantedTags");
            ResolveTags(effect.ApplicationRequiredTags, $"{path}.applicationRequiredTags");
            ResolveTags(effect.ApplicationImmunityTags, $"{path}.applicationImmunityTags");
            VisitGroup(effect.PeriodicEffects, $"{path}.periodicEffects");
            // Modifiers 无标签位。
        }

        static void VisitTrigger(TriggerDef trigger, string path)
        {
            if (trigger == null) return;
            ResolveTag(trigger.EventTag, $"{path}.eventTag");
            VisitCondition(trigger.Filter, $"{path}.filter");
            VisitGroup(trigger.Effects, $"{path}.effects");
        }

        static void VisitCardEntry(CardEntry entry, string path)
        {
            if (entry == null) return;
            VisitBattleCard(entry.Card, $"{path}.card");
        }

        static void VisitBattleCard(BattleCard card, string path)
        {
            if (card?.Effect != null)
                VisitList(card.Effect.Effects, $"{path}.effects", VisitGroup);
            // 怪兽卡体无标签位。
        }

        static void VisitList<T>(T[] items, string path, Action<T, string> visit)
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
                visit(items[i], $"{path}[{i}]");
        }

        static void ResolveTags(string[] tags, string path)
        {
            if (tags == null) return;
            for (int i = 0; i < tags.Length; i++)
                ResolveTag(tags[i], $"{path}[{i}]");
        }

        static void ResolveTag(string tag, string path)
        {
            if (string.IsNullOrEmpty(tag))
                throw new InvalidOperationException($"数据巡检失败：{path} 标签名为空（配置错误，加载期暴露，不带病运行）");
            if (!TagRegistry.TryResolve(tag, out _))
                throw new InvalidOperationException(
                    $"数据巡检失败：{path} 未声明的标签 \"{tag}\"——先 Declare 再反序列化（配置错误，加载期暴露，不带病运行）");
        }
    }
}
