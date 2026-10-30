using System;
using System.Collections.Generic;
using Battle;

namespace Fusion
{
    /// <summary>
    /// 融合数据加载入口（M5·T6）：吃 parts.json / sleeves.json 文本，吐按 id 可查的部位卡与卡套集合。
    /// CardCatalog 同款纪律：零 IO（读文件在调用方）；加载期全量校验，配置错误即抛、不带病运行——
    /// 复用 Battle 序列化设置（SkillJson：判别字段/枚举字符串/未知即抛）与巡检器（标签位逐个 Resolve），
    /// 另做数据级槽型×内容矩阵预检（装配期 Install 校验的前置防线）。
    /// </summary>
    public sealed class FusionCatalog
    {
        readonly Dictionary<int, PartCardConfig> partsById;
        readonly Dictionary<int, MonsterCardConfig> sleevesById;
        readonly List<PartCardConfig> parts;
        readonly List<MonsterCardConfig> sleeves;

        static FusionCatalog()
        {
            // 与 CardCatalog 相同的引导前置（幂等）：内置标签先声明，巡检才不误报
            SkillTags.EnsureDeclared();
            BattleEventTags.EnsureDeclared();
        }

        FusionCatalog(Dictionary<int, PartCardConfig> partsById, List<PartCardConfig> parts,
            Dictionary<int, MonsterCardConfig> sleevesById, List<MonsterCardConfig> sleeves)
        {
            this.partsById = partsById;
            this.parts = parts;
            this.sleevesById = sleevesById;
            this.sleeves = sleeves;
        }

        public IReadOnlyList<PartCardConfig> Parts => parts;
        public IReadOnlyList<MonsterCardConfig> Sleeves => sleeves;

        public PartCardConfig GetPart(int id) =>
            partsById.TryGetValue(id, out PartCardConfig part)
                ? part
                : throw new InvalidOperationException($"部位卡 id 未定义：{id}——先在 GameData/parts.json 配置（配置错误）");

        public MonsterCardConfig GetSleeve(int id) =>
            sleevesById.TryGetValue(id, out MonsterCardConfig sleeve)
                ? sleeve
                : throw new InvalidOperationException($"卡套 id 未定义：{id}——先在 GameData/sleeves.json 配置（配置错误）");

        public static FusionCatalog Load(string partsJson, string sleevesJson)
        {
            PartCardConfig[] partDefs = SkillJson.Deserialize<PartCardConfig[]>(partsJson) ?? Array.Empty<PartCardConfig>();
            Dictionary<int, PartCardConfig> partsById = new Dictionary<int, PartCardConfig>();
            List<PartCardConfig> parts = new List<PartCardConfig>();
            foreach (PartCardConfig part in partDefs)
            {
                if (part == null || part.Id <= 0)
                    throw new InvalidOperationException("部位卡 id 非法（须为正整数）——配置错误，加载期暴露");
                if (partsById.ContainsKey(part.Id))
                    throw new InvalidOperationException($"部位卡 id 重复：{part.Id}——配置错误，加载期暴露");
                CheckMatrix(part);
                // 标签与嵌套载荷巡检（复用 Battle 巡检器：数组入口是其支持形态，报错带数据路径）
                SkillJsonValidator.Validate(part.Effects);
                SkillJsonValidator.Validate(part.Triggers);
                SkillJsonValidator.Validate(part.Auras);
                if (part.GrantTags != null)
                    foreach (string tag in part.GrantTags)
                        if (!string.IsNullOrEmpty(tag)) TagRegistry.Resolve(tag);
                partsById.Add(part.Id, part);
                parts.Add(part);
            }

            MonsterCardConfig[] sleeveDefs =
                SkillJson.Deserialize<MonsterCardConfig[]>(sleevesJson) ?? Array.Empty<MonsterCardConfig>();
            Dictionary<int, MonsterCardConfig> sleevesById = new Dictionary<int, MonsterCardConfig>();
            List<MonsterCardConfig> sleeves = new List<MonsterCardConfig>();
            foreach (MonsterCardConfig sleeve in sleeveDefs)
            {
                if (sleeve == null || sleeve.Id <= 0)
                    throw new InvalidOperationException("卡套 id 非法（须为正整数）——配置错误，加载期暴露");
                if (sleevesById.ContainsKey(sleeve.Id))
                    throw new InvalidOperationException($"卡套 id 重复：{sleeve.Id}——配置错误，加载期暴露");
                if (sleeve.Slots == null || sleeve.Slots.Length == 0)
                    throw new InvalidOperationException($"卡套 {sleeve.Id}（{sleeve.Name}）slots 为空——配置错误，加载期暴露");
                if (sleeve.Cost == null) sleeve.Cost = new CostFormula();
                if (sleeve.StatMultiplier <= 0)
                    throw new InvalidOperationException($"卡套 {sleeve.Id}（{sleeve.Name}）statMultiplier 必须为正——配置错误，加载期暴露");
                sleevesById.Add(sleeve.Id, sleeve);
                sleeves.Add(sleeve);
            }
            return new FusionCatalog(partsById, parts, sleevesById, sleeves);
        }

        /// <summary>数据级槽型×内容矩阵预检（与 MonsterSleeve.Install 的装配期校验同规则——坏数据在加载期先暴露）。</summary>
        static void CheckMatrix(PartCardConfig part)
        {
            bool hasBody = part.Attack != 0 || part.MaxHp != 0;
            bool hasEffects = HasContent(part.Effects);
            bool hasContinuous = HasContent(part.Triggers) || HasContent(part.Auras);

            if (part.SlotType == SlotType.Body && (hasEffects || hasContinuous))
                throw new InvalidOperationException(
                    $"身材槽部位卡[{part.Name}]不得携带行为载荷（Effects/Triggers/Auras）——配置错误，加载期暴露");
            if (part.SlotType != SlotType.Body && hasBody)
                throw new InvalidOperationException(
                    $"效果槽/常驻槽部位卡[{part.Name}]不得携带身材数值（Attack/MaxHp）——配置错误，加载期暴露");
            if (part.SlotType == SlotType.Continuous && hasEffects)
                throw new InvalidOperationException(
                    $"常驻槽部位卡[{part.Name}]的行为应走 Triggers/Auras，不用 Effects——配置错误，加载期暴露");
            if ((part.SlotType == SlotType.OnSummon || part.SlotType == SlotType.OnLeave) && hasContinuous)
                throw new InvalidOperationException(
                    $"效果槽部位卡[{part.Name}]不得携带 Triggers/Auras——配置错误，加载期暴露");
        }

        static bool HasContent<T>(T[] array) => array != null && array.Length > 0;
    }
}
