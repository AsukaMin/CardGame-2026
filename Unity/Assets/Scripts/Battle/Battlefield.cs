using System.Collections.Generic;

namespace Battle
{
    /// <summary>双方场面：单位索引与阵营查询（默认不设席位上限）。</summary>
    public class Battlefield
    {
        readonly List<BattleUnit> units = new List<BattleUnit>();

        public IReadOnlyList<BattleUnit> AllUnits => units;

        public void Add(BattleUnit unit) => units.Add(unit);

        public void Remove(BattleUnit unit) => units.Remove(unit);

        public BattleUnit GetUnit(int id)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].Id == id) return units[i];
            return null;
        }

        public List<BattleUnit> UnitsOf(Side side)
        {
            List<BattleUnit> result = new List<BattleUnit>();
            for (int i = 0; i < units.Count; i++)
                if (units[i].Owner == side) result.Add(units[i]);
            return result;
        }

        public int CountOf(Side side)
        {
            int n = 0;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Owner == side) n++;
            return n;
        }

        /// <summary>某阵营是否存在嘲讽单位（嘲讽只约束攻击目标，双方对称）。</summary>
        public bool AnyTaunt(Side side)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].Owner == side && units[i].HasKeyword(Keywords.Taunt)) return true;
            return false;
        }
    }
}
