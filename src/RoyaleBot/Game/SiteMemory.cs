namespace Royale
{
    /// <summary>
    /// Помнит золото и максимальный размер шахты каждого сайта: рефери показывает их только для своих построек
    /// и сайтов ближе 300 к моей королеве. Заполняет KnownGold/KnownMaxMineSize в TurnInput.Sites.
    /// </summary>
    public sealed class SiteMemory
    {
        private int[] _gold, _max;

        public void Apply(TurnInput t)
        {
            if (_gold == null || _gold.Length != t.Sites.Length)
            {
                _gold = new int[t.Sites.Length];
                _max = new int[t.Sites.Length];
                for (int i = 0; i < _gold.Length; i++) { _gold[i] = -1; _max[i] = -1; }
            }
            for (int i = 0; i < t.Sites.Length; i++)
            {
                Site s = t.Sites[i];
                if (s.Gold >= 0) _gold[i] = s.Gold;
                if (s.MaxMineSize >= 0) _max[i] = s.MaxMineSize;
                s.KnownGold = _gold[i];
                s.KnownMaxMineSize = _max[i];
                t.Sites[i] = s;
            }
        }
    }
}
