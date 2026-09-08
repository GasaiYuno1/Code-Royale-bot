using System;
using System.Collections.Generic;

namespace Royale
{
    /// <summary>
    /// Случайный бот для логов сверки симулятора: строит что попало из разрешённого в лиге (в том числе казармы
    /// лучников и гигантов), тренирует всё, ходит куда попало. Только легальные по синтаксису ответы.
    /// </summary>
    public sealed class RandomStrategy : IStrategy
    {
        private readonly Random _rnd;

        public RandomStrategy(int seed) { _rnd = new Random(seed); }

        public void WarmUp(TurnClock clock) { }

        public TurnOutput Play(TurnInput t, TurnClock clock)
        {
            var o = new TurnOutput { Queen = QueenAction.Wait(), Train = new int[0] };
            var ids = new List<int>();
            int gold = t.Gold;
            foreach (Site s in t.Sites)
            {
                if (!s.IsOwn || s.Structure != StructureType.Barracks || !s.BarracksIdle) continue;
                if (_rnd.NextDouble() < 0.3) continue;
                int cost = CreepStats.Cost[s.Param2];
                if (gold < cost) continue;
                ids.Add(s.Id);
                gold -= cost;
            }
            o.Train = ids.ToArray();

            int r = _rnd.Next(100);
            if (r < 10) return o;
            if (r < 35)
            {
                o.Queen = QueenAction.Move(_rnd.Next(Consts.WorldWidth + 1), _rnd.Next(Consts.WorldHeight + 1));
                return o;
            }

            var allowed = new List<BuildType>();
            allowed.Add(BuildType.BarracksKnight);
            allowed.Add(BuildType.BarracksArcher);
            if (Rules.Giants) allowed.Add(BuildType.BarracksGiant);
            if (Rules.Towers) { allowed.Add(BuildType.Tower); allowed.Add(BuildType.Tower); }
            if (Rules.Mines) { allowed.Add(BuildType.Mine); allowed.Add(BuildType.Mine); }

            UnitInfo q = t.MyQueen;
            int idx;
            if (_rnd.Next(100) < 70)
            {
                var near = new List<int>();
                for (int i = 0; i < t.Sites.Length; i++) near.Add(i);
                near.Sort((x, y) => Geom.Dist2(t.Sites[x].X, t.Sites[x].Y, q.X, q.Y).CompareTo(Geom.Dist2(t.Sites[y].X, t.Sites[y].Y, q.X, q.Y)));
                idx = near[_rnd.Next(Math.Min(5, near.Count))];
            }
            else idx = _rnd.Next(t.Sites.Length);
            o.Queen = QueenAction.BuildAt(t.Sites[idx].Id, allowed[_rnd.Next(allowed.Count)]);
            return o;
        }
    }
}
