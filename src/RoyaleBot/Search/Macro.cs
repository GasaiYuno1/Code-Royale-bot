using System;
using System.Collections.Generic;

namespace Royale
{
    /// <summary>
    /// Макро-решения вне поиска: что тренировать и какие постройки предлагать поиску как кандидаты.
    /// Постройки бесплатны, золото тратится только на крипов, поэтому TRAIN — простая эвристика по золоту.
    /// </summary>
    public sealed class Macro
    {
        public int NearSites = 4;          // сколько ближайших сайтов предлагать под постройки
        public int GiantWhenTowers = 2;    // от скольких чужих башен нужны гиганты
        public int SecondBarracksIncome = 6;
        public int KnightZone = 250;       // не строить шахту, если чужой рыцарь ближе

        private readonly List<int> _train = new List<int>();
        // буферы под TRAIN по длине, отдельно для каждого игрока: без аллокаций в поиске (массив читается только внутри Step)
        private readonly int[][][] _trainBuf = { new int[16][], new int[16][] };
        private readonly int[] _near = new int[8];
        private readonly double[] _nearD = new double[8];

        /// <summary>TRAIN: гигант при нужде, потом все свободные казармы рыцарей, пока хватает золота.</summary>
        public int[] Train(SimState s, int me)
        {
            _train.Clear();
            int gold = s.Gold[me];
            int enemyTowers = 0;
            for (int i = 0; i < s.Sites.Length; i++)
                if (s.Sites[i].Structure == StructureType.Tower && s.Sites[i].Owner != me) enemyTowers++;
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (st.Structure != StructureType.Barracks || st.Owner != me || st.Training || st.CreepType != 2) continue;
                if (enemyTowers >= GiantWhenTowers && gold >= CreepStats.Cost[2]) { _train.Add(st.Id); gold -= CreepStats.Cost[2]; }
            }
            // копим на гиганта: пока у врага >= GiantWhenTowers башен и есть своя казарма гигантов, рыцарей тренируем только сверх 140
            bool saveForGiant = false;
            if (enemyTowers >= GiantWhenTowers)
                for (int i = 0; i < s.Sites.Length; i++)
                {
                    SimSite st = s.Sites[i];
                    if (st.Structure == StructureType.Barracks && st.Owner == me && st.CreepType == 2 && gold < CreepStats.Cost[2]) saveForGiant = true;
                }
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (st.Structure != StructureType.Barracks || st.Owner != me || st.Training || st.CreepType != 0) continue;
                if (saveForGiant) continue;
                if (gold >= CreepStats.Cost[0]) { _train.Add(st.Id); gold -= CreepStats.Cost[0]; }
            }
            if (_train.Count == 0) return SimAction.NoTrain;
            int[][] pool = _trainBuf[me];
            int n = Math.Min(_train.Count, pool.Length - 1);
            int[] buf = pool[n];
            if (buf == null) pool[n] = buf = new int[n];
            for (int i = 0; i < n; i++) buf[i] = _train[i];
            return buf;
        }

        /// <summary>Кандидаты действий королевы: WAIT, 8 направлений, постройки на ближайших сайтах. Возвращает число.</summary>
        public int Candidates(SimState s, int me, QueenAction[] buf)
        {
            int n = 0;
            buf[n++] = QueenAction.Wait();
            int qx = (int)s.QueenX[me], qy = (int)s.QueenY[me];
            for (int d = 0; d < 8; d++)
            {
                buf[n++] = QueenAction.Move(qx + Dx[d], qy + Dy[d]);
            }

            int knightBarracks = 0, giantBarracks = 0, enemyTowers = 0, income = 0;
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (st.Structure == StructureType.Tower && st.Owner != me) enemyTowers++;
                if (st.Owner != me) continue;
                if (st.Structure == StructureType.Barracks) { if (st.CreepType == 0) knightBarracks++; else if (st.CreepType == 2) giantBarracks++; }
                else if (st.Structure == StructureType.Mine) income += st.Rate;
            }
            int wantKnightBarracks = 1 + (income >= SecondBarracksIncome ? 1 : 0);

            int k = Math.Min(NearSites, _near.Length);
            int found = 0;
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (st.Structure == StructureType.Tower && st.Owner != me) continue;
                double d = SimState.D2(st.X, st.Y, s.QueenX[me], s.QueenY[me]);
                int pos = found < k ? found++ : -1;
                if (pos < 0)
                {
                    int worst = 0;
                    for (int j = 1; j < k; j++) if (_nearD[j] > _nearD[worst]) worst = j;
                    if (d >= _nearD[worst]) continue;
                    pos = worst;
                }
                _near[pos] = i; _nearD[pos] = d;
            }

            for (int j = 0; j < found && n < buf.Length - 5; j++)
            {
                SimSite st = s.Sites[_near[j]];
                bool takeable = st.Structure == StructureType.None || st.Owner != me;
                if (takeable)
                {
                    if (Rules.Mines && st.Gold != 0 && !EnemyKnightNear(s, me, st.X, st.Y)) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Mine);
                    if (Rules.Towers) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Tower);
                    if (knightBarracks < wantKnightBarracks) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.BarracksKnight);
                    if (Rules.Giants && giantBarracks == 0 && enemyTowers >= GiantWhenTowers) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.BarracksGiant);
                }
                else if (st.Structure == StructureType.Mine)
                {
                    if (st.MaxMineSize < 0 || st.Rate < st.MaxMineSize) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Mine);
                }
                else if (st.Structure == StructureType.Tower)
                {
                    if (st.Hp <= Consts.TowerHpMax - Consts.TowerHpIncrement) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Tower);
                }
            }
            return n;
        }

        private bool EnemyKnightNear(SimState s, int me, int x, int y)
        {
            int e = 1 - me;
            double r2 = (double)KnightZone * KnightZone;
            for (int i = 0; i < s.CreepCount[e]; i++)
                if (s.Creeps[e][i].Type == 0 && SimState.D2(s.Creeps[e][i].X, s.Creeps[e][i].Y, x, y) < r2) return true;
            return false;
        }

        private static readonly int[] Dx = { 100, 71, 0, -71, -100, -71, 0, 71 };
        private static readonly int[] Dy = { 0, 71, 100, 71, 0, -71, -100, -71 };
    }
}
