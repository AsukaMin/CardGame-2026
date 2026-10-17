using System;
using Battle;
using UnityEngine.UIElements;

namespace CardAuthoring.Editor
{
    /// <summary>
    /// 卡级表单工厂：效果卡（id/工作名/档位管理）与部位卡（id/工作名/槽型/身材/关键词/消耗）。
    /// 直接写回资产对象 + onChange 置脏（与参考项目一致的"直接写数据"方式，不走 SerializedProperty）。
    /// </summary>
    public static class CardInspectorView
    {
        public static VisualElement Build(EffectCardAsset asset, Action onChange, Action<int> onTierSwitched, int activeTier)
        {
            VisualElement root = new VisualElement();
            root.Add(SectionTitle($"效果卡 #{asset.id}"));

            var id = new IntegerField("卡牌 Id") { value = asset.id };
            id.RegisterValueChangedCallback(evt => { asset.id = evt.newValue; onChange(); });
            root.Add(id);

            var name = new TextField("工作名（正式名称归表格）") { value = asset.cardName };
            name.RegisterValueChangedCallback(evt => { asset.cardName = evt.newValue; onChange(); });
            root.Add(name);

            root.Add(SectionTitle("充能档位（每档一张独立的图）"));
            for (int i = 0; i < asset.tiers.Count; i++)
            {
                int index = i;
                EffectCardAsset.TierData tier = asset.tiers[i];

                VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
                var open = new Button(() => onTierSwitched(index)) { text = i == activeTier ? "●" : "○", style = { width = 28 } };
                var label = new TextField { value = tier.label, style = { flexGrow = 1, minWidth = 60 } };
                label.RegisterValueChangedCallback(evt => { tier.label = evt.newValue; onChange(); });
                var cost = new IntegerField { value = tier.cost, style = { width = 64 } };
                cost.RegisterValueChangedCallback(evt => { tier.cost = evt.newValue; onChange(); });
                var remove = new Button(() =>
                {
                    if (asset.tiers.Count <= 1) return; // 至少保留一档
                    asset.tiers.RemoveAt(index);
                    onChange();
                    onTierSwitched(Math.Min(index, asset.tiers.Count - 1));
                }) { text = "×", style = { width = 24 } };
                row.Add(open);
                row.Add(label);
                row.Add(cost);
                row.Add(remove);
                root.Add(row);
            }

            var addTier = new Button(() =>
            {
                asset.tiers.Add(new EffectCardAsset.TierData
                {
                    cost = asset.tiers.Count > 0 ? asset.tiers[asset.tiers.Count - 1].cost * 2 : 2,
                    label = $"档位{asset.tiers.Count + 1}",
                });
                onChange();
                onTierSwitched(asset.tiers.Count - 1);
            }) { text = "+ 添加档位" };
            root.Add(addTier);
            return root;
        }

        public static VisualElement Build(PartCardAsset asset, Action onChange)
        {
            VisualElement root = new VisualElement();
            root.Add(SectionTitle($"部位卡 #{asset.id}"));

            var id = new IntegerField("卡牌 Id") { value = asset.id };
            id.RegisterValueChangedCallback(evt => { asset.id = evt.newValue; onChange(); });
            root.Add(id);

            var name = new TextField("工作名（正式名称归表格）") { value = asset.cardName };
            name.RegisterValueChangedCallback(evt => { asset.cardName = evt.newValue; onChange(); });
            root.Add(name);

            var slot = new EnumField("槽型", asset.slotType);
            slot.RegisterValueChangedCallback(evt => { asset.slotType = (SlotType)evt.newValue; onChange(); });
            root.Add(slot);

            var cost = new IntegerField("费用") { value = asset.cost };
            cost.RegisterValueChangedCallback(evt => { asset.cost = evt.newValue; onChange(); });
            root.Add(cost);

            var attack = new IntegerField("攻击（身材槽生效）") { value = asset.attack };
            attack.RegisterValueChangedCallback(evt => { asset.attack = evt.newValue; onChange(); });
            root.Add(attack);

            var maxHp = new IntegerField("生命（身材槽生效）") { value = asset.maxHp };
            maxHp.RegisterValueChangedCallback(evt => { asset.maxHp = evt.newValue; onChange(); });
            root.Add(maxHp);

            var keywords = new EnumField("随召唤赋予关键词", asset.grantKeywords);
            keywords.RegisterValueChangedCallback(evt => { asset.grantKeywords = (Keywords)evt.newValue; onChange(); });
            root.Add(keywords);

            var consumable = new Toggle("消耗（死亡销毁，战后不回收）") { value = asset.consumable };
            consumable.RegisterValueChangedCallback(evt => { asset.consumable = evt.newValue; onChange(); });
            root.Add(consumable);

            root.Add(new Label("效果图在中间画布编辑（槽型为身材时通常留空）")
            {
                style = { color = UnityEngine.Color.gray, whiteSpace = UnityEngine.UIElements.WhiteSpace.Normal },
            });
            return root;
        }

        static Label SectionTitle(string text) => new Label(text)
        {
            style =
            {
                unityFontStyleAndWeight = UnityEngine.FontStyle.Bold,
                marginTop = 8,
                marginBottom = 4,
            },
        };
    }
}
