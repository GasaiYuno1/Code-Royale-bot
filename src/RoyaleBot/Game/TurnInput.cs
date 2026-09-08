namespace Royale
{
    /// <summary>Состояние одного хода как во вводе, плюс удобные срезы.</summary>
    public sealed class TurnInput
    {
        public int Gold;
        public int TouchedSite;      // -1, если королева не касается сайта
        public Site[] Sites;         // в порядке ввода (id идут 0..n-1)
        public UnitInfo[] Units;     // в порядке ввода: крипы игрока, его королева, крипы противника, его королева
        public UnitInfo MyQueen, EnemyQueen;

        /// <summary>Суммарный доход моих шахт.</summary>
        public int Income
        {
            get
            {
                int s = 0;
                foreach (Site site in Sites) if (site.IsOwnMine && site.Param1 > 0) s += site.Param1;
                return s;
            }
        }

        public int CountOwn(StructureType t)
        {
            int n = 0;
            foreach (Site site in Sites) if (site.IsOwn && site.Structure == t) n++;
            return n;
        }

        public int IndexOfSite(int id)
        {
            if (id >= 0 && id < Sites.Length && Sites[id].Id == id) return id;
            for (int i = 0; i < Sites.Length; i++) if (Sites[i].Id == id) return i;
            return -1;
        }
    }
}
