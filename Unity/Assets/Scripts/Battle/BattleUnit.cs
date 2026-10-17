using System;
using System.Collections.Generic;

namespace Battle
{
    /// <summary>
    /// 场上战斗单位 = 已召唤的怪兽卡套实例。
    /// 基础攻血来自身材槽部位卡求和×卡套系数；临时修饰与光环修饰分桶，互不叠加污染。
    /// </summary>
    public class BattleUnit
    {
        readonly List<StatModifier> transient = new List<StatModifier>();
        readonly List<StatModifier> auras = new List<StatModifier>();

        public int Id;
        public Side Owner;
        public string Name = "";
        public bool Alive = true;
        /// <summary>本回合是否已攻击（每怪每回合 1 次）。</summary>
        public bool AttackedThisTurn;
        public int BaseAttack;
        public int BaseMaxHp;
        public int Hp;
        /// <summary>随召唤赋予的关键词（部位卡 GrantKeywords 之和）。</summary>
        public Keywords BaseKeywords = Keywords.None;
        /// <summary>来源卡套（死亡时整包进墓地）。</summary>
        public MonsterSleeve Sleeve;

        public int EffectiveAttack
        {
            get
            {
                int v = BaseAttack;
                v += SumStat(transient, StatType.Attack);
                v += SumStat(auras, StatType.Attack);
                return v;
            }
        }

        public int EffectiveMaxHp
        {
            get
            {
                int v = BaseMaxHp;
                v += SumStat(transient, StatType.MaxHp);
                v += SumStat(auras, StatType.MaxHp);
                return Math.Max(1, v);
            }
        }

        public bool HasKeyword(Keywords keyword) =>
            BaseKeywords.HasFlag(keyword) || HasKeywordIn(transient, keyword) || HasKeywordIn(auras, keyword);

        /// <summary>施加临时修饰：血上限增益同步抬高当前血，减益随后夹紧。</summary>
        public void AddTransient(StatModifier mod)
        {
            transient.Add(mod);
            if (!mod.IsKeywordMod && mod.Stat == StatType.MaxHp && mod.Amount > 0)
                Hp += mod.Amount;
            ClampHp();
        }

        /// <summary>回合推进：Turns 类修饰递减并移除归零项。</summary>
        public void TickTurnDurations()
        {
            transient.RemoveAll(m => m.Kind == ModifierDurationKind.Turns && --m.RemainingTurns <= 0);
            ClampHp();
        }

        /// <summary>回合结束：清空 ThisTurn 修饰。</summary>
        public void ExpireThisTurn()
        {
            transient.RemoveAll(m => m.Kind == ModifierDurationKind.ThisTurn);
            ClampHp();
        }

        /// <summary>光环桶：战场重算时先清后加。血上限变化只作上限、不附带抬血。</summary>
        public void ClearAuras() { auras.Clear(); ClampHp(); }

        public void AddAura(StatModifier mod) { auras.Add(mod); ClampHp(); }

        public void ResetAttackFlag() => AttackedThisTurn = false;

        public void ClampHp() => Hp = Math.Max(0, Math.Min(Hp, EffectiveMaxHp));

        static int SumStat(List<StatModifier> list, StatType stat)
        {
            int v = 0;
            for (int i = 0; i < list.Count; i++)
            {
                StatModifier m = list[i];
                if (!m.IsKeywordMod && m.Stat == stat) v += m.Amount;
            }
            return v;
        }

        static bool HasKeywordIn(List<StatModifier> list, Keywords keyword)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsKeywordMod && list[i].Keyword.HasFlag(keyword)) return true;
            return false;
        }
    }
}
