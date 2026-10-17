using System.Collections.Generic;
using System.IO;
using System.Linq;
using Battle;
using UnityEditor;
using UnityEngine;

namespace CardAuthoring.Editor
{
    /// <summary>
    /// 卡池导出器：SO 编辑源 → GameData/effect_cards.json + part_cards.json。
    /// 编辑器侧唯一持有 GameData 路径的类（运行时路径纪律四持有者不变，此为编辑期第五个）。
    /// 导出语义：重复 id 报错中止；孤立节点/空档位告警后继续（孤立节点被编译器跳过）。
    /// </summary>
    public static class CardPoolExporter
    {
        [MenuItem("Tools/技能编辑器/导出卡池 JSON")]
        public static void ExportFromMenu()
        {
            // 打开中的窗口先保存，保证图结构与 opJson 落盘
            CardEditorWindow open = Resources.FindObjectsOfTypeAll<CardEditorWindow>().FirstOrDefault();
            if (open != null) open.Save();
            ExportAll();
        }

        public static void ExportAll()
        {
            List<EffectCardConfig> cards = CollectEffectCards();
            if (cards == null) return;
            List<PartCardConfig> parts = CollectPartCards();
            if (parts == null) return;

            Directory.CreateDirectory(GameDataDir);
            string effectPath = Path.Combine(GameDataDir, "effect_cards.json");
            string partPath = Path.Combine(GameDataDir, "part_cards.json");
            File.WriteAllText(effectPath, CardJsonWriter.WriteEffectCards(cards));
            File.WriteAllText(partPath, CardJsonWriter.WritePartCards(parts));
            Debug.Log($"[卡池导出] 完成：效果卡 {cards.Count} 张 → {effectPath}；部位卡 {parts.Count} 张 → {partPath}。孤立/异常警告见上方日志。");
        }

        /// <summary>GameData 在 Unity 工程外、仓库根目录（目录结构文档：运行时 JSON 母本不进 Assets）。</summary>
        static string GameDataDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "GameData"));

        static List<EffectCardConfig> CollectEffectCards()
        {
            List<EffectCardAsset> assets = LoadAll<EffectCardAsset>("t:EffectCardAsset");
            HashSet<int> seenIds = new HashSet<int>();
            List<EffectCardConfig> result = new List<EffectCardConfig>();

            foreach (EffectCardAsset asset in assets)
            {
                if (!seenIds.Add(asset.id))
                {
                    Debug.LogError($"[卡池导出] 效果卡 id 重复：{asset.id}（{asset.name} 与其他卡冲突），导出中止。请修正后重试。");
                    return null;
                }
                EffectCardConfig config = new EffectCardConfig
                {
                    Id = asset.id,
                    Name = asset.cardName,
                    Tiers = new EffectTier[asset.tiers.Count],
                };
                for (int i = 0; i < asset.tiers.Count; i++)
                {
                    EffectCardAsset.TierData tier = asset.tiers[i];
                    GraphCompiler.Result compile =
                        GraphCompiler.Compile(tier.effectDefs, tier.opNodes, tier.connections);
                    LogWarnings($"{asset.name}·{tier.label}", compile.Warnings);
                    config.Tiers[i] = new EffectTier
                    {
                        Cost = tier.cost,
                        Label = tier.label,
                        Effects = compile.Effects.ToArray(),
                    };
                }
                result.Add(config);
            }
            return result;
        }

        static List<PartCardConfig> CollectPartCards()
        {
            List<PartCardAsset> assets = LoadAll<PartCardAsset>("t:PartCardAsset");
            HashSet<int> seenIds = new HashSet<int>();
            List<PartCardConfig> result = new List<PartCardConfig>();

            foreach (PartCardAsset asset in assets)
            {
                if (!seenIds.Add(asset.id))
                {
                    Debug.LogError($"[卡池导出] 部位卡 id 重复：{asset.id}（{asset.name} 与其他卡冲突），导出中止。请修正后重试。");
                    return null;
                }
                GraphCompiler.Result compile =
                    GraphCompiler.Compile(asset.effectDefs, asset.opNodes, asset.connections);
                LogWarnings(asset.name, compile.Warnings);
                result.Add(new PartCardConfig
                {
                    Id = asset.id,
                    Name = asset.cardName,
                    SlotType = asset.slotType,
                    Cost = asset.cost,
                    Attack = asset.attack,
                    MaxHp = asset.maxHp,
                    GrantKeywords = asset.grantKeywords,
                    Consumable = asset.consumable,
                    Effects = compile.Effects.ToArray(),
                });
            }
            return result;
        }

        static void LogWarnings(string context, List<string> warnings)
        {
            foreach (string warning in warnings)
                Debug.LogWarning($"[卡池导出] {context}：{warning}");
        }

        static List<T> LoadAll<T>(string filter) where T : ScriptableObject
        {
            CardEditorWindow.EnsureFolders();
            return AssetDatabase.FindAssets(filter, new[] { CardEditorWindow.AuthoringRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path)
                .Select(path => AssetDatabase.LoadAssetAtPath<T>(path))
                .Where(asset => asset != null)
                .ToList();
        }
    }
}
