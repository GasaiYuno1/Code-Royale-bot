using System;
using System.IO;

namespace Royale
{
    public static class InputParser
    {
        /// <summary>Стартовый ввод: numSites, затем по сайту "siteId x y radius". first — уже прочитанная первая строка.</summary>
        public static Site[] ReadInit(string first, TextReader r)
        {
            int n = int.Parse(first.Trim());
            var sites = new Site[n];
            for (int i = 0; i < n; i++)
            {
                string[] t = Split(ReadNonEmpty(r));
                var s = new Site();
                s.Id = int.Parse(t[0]);
                s.X = int.Parse(t[1]);
                s.Y = int.Parse(t[2]);
                s.Radius = int.Parse(t[3]);
                s.Gold = -1; s.MaxMineSize = -1; s.KnownGold = -1; s.KnownMaxMineSize = -1;
                s.Structure = StructureType.None; s.Owner = -1; s.Param1 = -1; s.Param2 = -1;
                sites[i] = s;
            }
            return sites;
        }

        /// <summary>Ввод хода: "gold touchedSite", по сайту 7 чисел, numUnits, по юниту 5 чисел. first — первая строка хода.</summary>
        public static TurnInput ReadTurn(string first, TextReader r, Site[] init)
        {
            string[] t = Split(first);
            var turn = new TurnInput();
            turn.Gold = int.Parse(t[0]);
            turn.TouchedSite = int.Parse(t[1]);
            turn.Sites = new Site[init.Length];
            for (int i = 0; i < init.Length; i++)
            {
                t = Split(ReadNonEmpty(r));
                int id = int.Parse(t[0]);
                int idx = i;
                if (init[idx].Id != id)
                {
                    idx = -1;
                    for (int k = 0; k < init.Length; k++) if (init[k].Id == id) { idx = k; break; }
                    if (idx < 0) throw new FormatException("unknown site id " + id);
                }
                Site s = init[idx];
                s.Gold = int.Parse(t[1]);
                s.MaxMineSize = int.Parse(t[2]);
                s.Structure = (StructureType)int.Parse(t[3]);
                s.Owner = int.Parse(t[4]);
                s.Param1 = int.Parse(t[5]);
                s.Param2 = int.Parse(t[6]);
                turn.Sites[idx] = s;
            }
            int m = int.Parse(ReadNonEmpty(r).Trim());
            turn.Units = new UnitInfo[m];
            for (int i = 0; i < m; i++)
            {
                t = Split(ReadNonEmpty(r));
                var u = new UnitInfo();
                u.X = int.Parse(t[0]);
                u.Y = int.Parse(t[1]);
                u.Owner = int.Parse(t[2]);
                u.Type = (UnitType)int.Parse(t[3]);
                u.Health = int.Parse(t[4]);
                turn.Units[i] = u;
                if (u.IsQueen)
                {
                    if (u.Owner == 0) turn.MyQueen = u; else turn.EnemyQueen = u;
                }
            }
            return turn;
        }

        private static string ReadNonEmpty(TextReader r)
        {
            while (true)
            {
                string line = r.ReadLine();
                if (line == null) throw new EndOfStreamException("unexpected end of input");
                if (line.Trim().Length > 0) return line;
            }
        }

        private static string[] Split(string line)
        {
            return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
