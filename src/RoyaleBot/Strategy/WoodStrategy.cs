using System;
using System.Collections.Generic;

namespace Royale
{
    /// <summary>
    /// Простая эвристика для выхода из Wood (и запасной вариант на случай ошибки поиска):
    /// казарма рыцарей, шахты там, где нет фиксированного дохода, башни у своей королевы,
    /// при подходе чужих рыцарей — под свою башню и качать её, иначе отходить к дому.
    /// Строит только то, что разрешено в Rules.
    /// </summary>
    public sealed class WoodStrategy : IStrategy
    {
        public int TargetIncome = 5;        // до какого дохода наращивать шахты, прежде чем строить башни
        public int TargetTowers = 3;
        public int TowerUpgradeBelow = 500;
        public int DangerRadius = 400;      // чужие рыцари ближе этого — отходим
        public int EnemyZone = 350;         // сайты ближе этого к чужой королеве не берём
        public int KnightZone = 250;        // сайты с чужими рыцарями ближе этого не берём

        private int _homeX = -1, _homeY = -1;

        public void WarmUp(TurnClock clock) { }

        public TurnOutput Play(TurnInput t, TurnClock clock)
        {
            UnitInfo q = t.MyQueen;
            if (_homeX < 0) { _homeX = q.X; _homeY = q.Y; }

            var o = new TurnOutput { Queen = QueenAction.Wait(), Train = ChooseTrain(t) };

            int kx = 0, ky = 0, kn = 0;
            foreach (UnitInfo u in t.Units)
            {
                if (u.Owner != 1 || u.Type != UnitType.Knight) continue;
                if (Geom.Dist(u.X, u.Y, q.X, q.Y) < DangerRadius) { kx += u.X; ky += u.Y; kn++; }
            }
            if (kn > 0)
            {
                o.Queen = Retreat(t, q, kx / kn, ky / kn);
                return o;
            }

            BuildType type;
            int idx = ChooseBuild(t, out type);
            if (idx >= 0 && QueenAction.Allowed(type)) o.Queen = QueenAction.BuildAt(t.Sites[idx].Id, type);
            return o;
        }

        private QueenAction Retreat(TurnInput t, UnitInfo q, int cx, int cy)
        {
            // Под самую крепкую свою башню поблизости: BUILD на неё = подойти и качать, стоя под прикрытием.
            int best = -1, bestHp = 0;
            for (int i = 0; i < t.Sites.Length; i++)
            {
                Site s = t.Sites[i];
                if (!s.IsOwnTower || s.Param1 <= bestHp) continue;
                if (Geom.Dist(s.X, s.Y, q.X, q.Y) > 800) continue;
                best = i; bestHp = s.Param1;
            }
            if (best >= 0 && Rules.Towers) return QueenAction.BuildAt(t.Sites[best].Id, BuildType.Tower);

            // Иначе от рыцарей с уклоном к дому.
            double ax = q.X - cx, ay = q.Y - cy;
            double al = Math.Sqrt(ax * ax + ay * ay);
            if (al < 1e-6) { ax = _homeX - q.X; ay = _homeY - q.Y; al = Math.Sqrt(ax * ax + ay * ay); }
            if (al < 1e-6) { ax = 1; ay = 0; al = 1; }
            ax /= al; ay /= al;
            double hx = _homeX - q.X, hy = _homeY - q.Y;
            double hl = Math.Sqrt(hx * hx + hy * hy);
            if (hl > 1e-6) { hx /= hl; hy /= hl; } else { hx = 0; hy = 0; }
            double dx = ax * 0.7 + hx * 0.3, dy = ay * 0.7 + hy * 0.3;
            double dl = Math.Sqrt(dx * dx + dy * dy);
            if (dl < 1e-6) { dx = ax; dy = ay; dl = 1; }
            dx /= dl; dy /= dl;
            int tx = Geom.Clamp((int)Math.Round(q.X + dx * 300), Consts.QueenRadius, Consts.WorldWidth - Consts.QueenRadius);
            int ty = Geom.Clamp((int)Math.Round(q.Y + dy * 300), Consts.QueenRadius, Consts.WorldHeight - Consts.QueenRadius);
            return QueenAction.Move(tx, ty);
        }

        private enum Filter { Empty, EmptyWithGold, OwnMineUpgradable }

        private int ChooseBuild(TurnInput t, out BuildType type)
        {
            type = BuildType.BarracksKnight;
            int knightBarracks = 0, towers = 0;
            foreach (Site s in t.Sites)
            {
                if (s.IsOwnBarracks(UnitType.Knight)) knightBarracks++;
                if (s.IsOwnTower) towers++;
            }

            if (knightBarracks == 0)
            {
                int i = Nearest(t, Filter.Empty);
                if (i >= 0) { type = BuildType.BarracksKnight; return i; }
            }

            if (Rules.Mines && t.Income < TargetIncome)
            {
                int i = Nearest(t, Filter.OwnMineUpgradable);
                if (i < 0) i = Nearest(t, Filter.EmptyWithGold);
                if (i >= 0) { type = BuildType.Mine; return i; }
            }

            if (Rules.Towers)
            {
                if (towers < TargetTowers)
                {
                    int i = Nearest(t, Filter.Empty);
                    if (i >= 0) { type = BuildType.Tower; return i; }
                }
                int weakest = -1, hp = int.MaxValue;
                for (int i = 0; i < t.Sites.Length; i++)
                {
                    Site s = t.Sites[i];
                    if (s.IsOwnTower && s.Param1 < TowerUpgradeBelow && s.Param1 < hp) { weakest = i; hp = s.Param1; }
                }
                if (weakest >= 0) { type = BuildType.Tower; return weakest; }
            }

            if (Rules.Mines && t.Income < TargetIncome + 3)
            {
                int i = Nearest(t, Filter.OwnMineUpgradable);
                if (i < 0) i = Nearest(t, Filter.EmptyWithGold);
                if (i >= 0) { type = BuildType.Mine; return i; }
            }
            return -1;
        }

        /// <summary>Ближайший к моей королеве сайт под фильтр, не у чужой королевы и не рядом с чужими рыцарями.</summary>
        private int Nearest(TurnInput t, Filter f)
        {
            UnitInfo q = t.MyQueen, eq = t.EnemyQueen;
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < t.Sites.Length; i++)
            {
                Site s = t.Sites[i];
                bool ok;
                switch (f)
                {
                    case Filter.Empty: ok = s.IsEmpty; break;
                    case Filter.EmptyWithGold: ok = s.IsEmpty && s.MayHaveGold; break;
                    default: ok = s.IsOwnMine && s.KnownMaxMineSize > 0 && s.Param1 < s.KnownMaxMineSize && s.MayHaveGold; break;
                }
                if (!ok) continue;
                if (Geom.Dist(s.X, s.Y, eq.X, eq.Y) < EnemyZone) continue;
                if (KnightNear(t, s.X, s.Y, KnightZone)) continue;
                double d = Geom.Dist(s.X, s.Y, q.X, q.Y);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static bool KnightNear(TurnInput t, int x, int y, int radius)
        {
            foreach (UnitInfo u in t.Units)
                if (u.Owner == 1 && u.Type == UnitType.Knight && Geom.Dist(u.X, u.Y, x, y) < radius) return true;
            return false;
        }

        private int[] ChooseTrain(TurnInput t)
        {
            var ids = new List<int>();
            int gold = t.Gold;
            foreach (Site s in t.Sites)
            {
                if (!s.IsOwnBarracks(UnitType.Knight) || !s.BarracksIdle) continue;
                if (gold < Creeps.Cost[(int)UnitType.Knight]) break;
                ids.Add(s.Id);
                gold -= Creeps.Cost[(int)UnitType.Knight];
            }
            return ids.ToArray();
        }
    }
}
