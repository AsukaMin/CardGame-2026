using System;

namespace Battle
{
    /// <summary>
    /// 效果卡：手牌打出的法术——打出→结算→消失（瞬间生命周期，不占场、无墓地循环）。
    /// M1 = 单档面值费用；充能档位（低配/完全体）M2+。
    /// 效果载荷 = 技能系统 Data 层（EffectGroupDef[]）——新卡=新数据，零代码改动。
    /// M5·缺口§2：Triggers = 打出即注册的触发器（"结界"形态，挂玩家领袖，随本场生灭）；
    /// 纯触发卡 Effects 可为空（载荷全在 Triggers）。
    /// </summary>
    [Serializable]
    public sealed class EffectCardDef
    {
        public string Name = "";
        public int Cost;
        public EffectGroupDef[] Effects = Array.Empty<EffectGroupDef>();
        public TriggerDef[] Triggers;
    }

    /// <summary>
    /// 怪兽卡：M1 血攻直给的最小形态（召唤→在场→攻击→死亡）。
    /// M5 融合域接入后由"装好部位卡的 MonsterSleeve"取代（卡套本体无数值）。
    /// </summary>
    [Serializable]
    public sealed class MonsterCardDef
    {
        public string Name = "";
        public int Cost;
        public int Attack;
        public int MaxHp;
    }

    /// <summary>手牌/牌库条目（效果卡/怪兽卡/组装怪兽三臂联合容器；M2 数据管线接管后替换为表驱动）。
    /// Id = 信封表行 id（CardJsonConverter 解析时注入；卡体不进 JSON，此字段纯运行时——归因与复制卡的卡身份；
    /// 组装怪兽臂的 Id = 卡套 id，由投影层填）。代码手搓的卡（测试夹具等）Id 可为 0。</summary>
    public sealed class BattleCard
    {
        public int Id;
        public EffectCardDef Effect;
        public MonsterCardDef Monster;
        /// <summary>组装怪兽（M5·T6 第三臂）：整备卡套的战斗投影——IsMonster 语义涵盖（可拖拽召唤）。</summary>
        public SummonPayload Summon;

        public string Name => Effect != null ? Effect.Name
            : Monster != null ? Monster.Name
            : Summon != null ? Summon.Name : "";
        public int Cost => Effect != null ? Effect.Cost
            : Monster != null ? Monster.Cost
            : Summon != null ? Summon.Cost : 0;
        public bool IsMonster => Monster != null || Summon != null;

        /// <summary>
        /// 是否依赖敌方主目标（M5·验收反馈：效果组里任一 Target==Chosen 即 true——直伤/单体指向卡）。
        /// true 的卡 PlayEffectCard 目标必须是敌方（防"直伤卡打自己"）；false 的卡（治疗/增益/AOE 类）
        /// 目标可以是任意存活实体（己方释放——"拖到自己场上=给自己/给该怪"）。纯数据推导，零配置。
        /// </summary>
        public bool RequiresEnemyTarget
        {
            get
            {
                if (Effect?.Effects == null) return false;
                foreach (EffectGroupDef group in Effect.Effects)
                    if (group != null && group.Target == TargetMode.Chosen) return true;
                return false;
            }
        }

        public static BattleCard Of(EffectCardDef effect) => new BattleCard { Effect = effect };
        public static BattleCard Of(MonsterCardDef monster) => new BattleCard { Monster = monster };
        public static BattleCard Of(SummonPayload summon) => new BattleCard { Summon = summon };
    }
}
