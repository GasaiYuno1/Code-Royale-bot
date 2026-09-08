namespace Royale
{
    /// <summary>
    /// Помнит золото и максимальный размер шахты каждого сайта: рефери показывает их только для своих построек
    /// и сайтов ближе 300 к моей королеве. Заполняет KnownGold/KnownMaxMineSize в TurnInput.Sites.
    /// Невидимые сайты: если шахта исчезла, а золота в ней оставалось не больше её дохода — она истощена (0),
    /// иначе снесена (золото на месте); чужая невидимая шахта продолжает копать — золото убывает на её доход.
    /// </summary>
    public sealed class SiteMemory
    {
        private int[] _gold, _max, _rate;
        private bool[] _wasMine;

        public void Apply(TurnInput t)
        {
            int n = t.Sites.Length;
            if (_gold == null || _gold.Length != n)
            {
                _gold = new int[n]; _max = new int[n]; _rate = new int[n]; _wasMine = new bool[n];
                for (int i = 0; i < n; i++) { _gold[i] = -1; _max[i] = -1; }
            }
            for (int i = 0; i < n; i++)
            {
                Site s = t.Sites[i];
                bool isMine = s.Structure == StructureType.Mine;
                if (s.Gold >= 0) _gold[i] = s.Gold;
                else if (_wasMine[i] && _gold[i] >= 0 && _rate[i] > 0)
                {
                    if (s.Structure == StructureType.None) { if (_gold[i] <= _rate[i]) _gold[i] = 0; }
                    else if (isMine) { _gold[i] -= _rate[i]; if (_gold[i] < 0) _gold[i] = 0; }
                }
                if (s.MaxMineSize >= 0) _max[i] = s.MaxMineSize;
                if (isMine) { if (s.Param1 >= 0) _rate[i] = s.Param1; }
                else _rate[i] = 0;
                _wasMine[i] = isMine;
                s.KnownGold = _gold[i];
                s.KnownMaxMineSize = _max[i];
                t.Sites[i] = s;
            }
        }
    }
}
