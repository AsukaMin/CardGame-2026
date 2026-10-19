using System;

namespace Battle
{
    /// <summary>
    /// 效果卡：手牌打出的法术——打出→结算→消失（瞬间生命周期，不占场、无墓地循环）。
    /// M1 = 单档面值费用；充能档位（低配/完全体）M2+。
    /// 效果载荷 = 技能系统 Data 层（EffectGroupDef[]）——新卡=新数据，零代码改动。
    /// </summary>
    [Serializable]
    public sealed class EffectCardDef
    {
        public string Name = "";
        public int Cost;
        public EffectGroupDef[] Effects = Array.Empty<EffectGroupDef>();
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

    /// <summary>手牌/牌库条目（效果卡与怪兽卡的联合容器；M2 数据管线接管后替换为表驱动）。</summary>
    public sealed class BattleCard
    {
        public EffectCardDef Effect;
        public MonsterCardDef Monster;

        public string Name => Effect != null ? Effect.Name : Monster != null ? Monster.Name : "";
        public int Cost => Effect != null ? Effect.Cost : Monster != null ? Monster.Cost : 0;
        public bool IsMonster => Monster != null;

        public static BattleCard Of(EffectCardDef effect) => new BattleCard { Effect = effect };
        public static BattleCard Of(MonsterCardDef monster) => new BattleCard { Monster = monster };
    }
}
