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
        public int NearSites = 3;          // сколько ближайших свободных сайтов предлагать под постройки (4 давало 23 кандидата на ход против 16 и глубину 3 вместо 12 на четверти ходов с крипами)
        public int NearOwn = 2;            // сколько ближайших своих сайтов предлагать под прокачку / замену
        public int BarracksSites = 2;      // казармы предлагаются только на стольких ближайших свободных сайтах
        public int GiantWhenTowers = 2;    // от скольких чужих башен нужны гиганты
        public int GiantSaveTurns = 15;    // копить на гиганта только если хватит за столько ходов при текущем доходе
        public int LowHpRush = 0;          // чужая королева не выше этого HP: не копим на гигантов и не фильтруем волну — добиваем рыцарями (ключ lowhp; 20 спасло партию с противником на 12 HP за 3 башнями, но против эталона 3:7 вместо 6:4 на тех же сидах — выключено)
        public int GiantMinLeft = 40;      // гигантов не тренируем и не копим на них, когда до конца партии меньше ходов (стройка 10 + ход 50/ход; исход решает HP, не башни)
        public int SecondBarracksIncome = 6;
        public int KnightZone = 250;       // не строить шахту, если чужой рыцарь ближе
        public int MaxTowersCalm = 3;      // без живых чужих рыцарей новые башни сверх этого не предлагаются
        public int TargetIncome = 6;       // пока доход меньше — фаза экономики: башни только при чужой казарме (до TowersEarly) или живых рыцарях
        public int TowersEarly = 2;
        public int UpgradeCalmBelow = 350; // без живых чужих рыцарей башню качаем только ниже этого HP
        public int FarMineSites = 2;       // всегда добавлять столько ближайших свободных сайтов с золотом (цель для похода)

        private readonly List<int> _train = new List<int>();
        // буферы под TRAIN по длине, отдельно для каждого игрока: без аллокаций в поиске (массив читается только внутри Step)
        private readonly int[][][] _trainBuf = { new int[16][], new int[16][] };
        private readonly int[] _near = new int[12];
        private readonly double[] _nearD = new double[12];

        /// <summary>TRAIN: гигант при нужде, потом все свободные казармы рыцарей, пока хватает золота.</summary>
        public int[] Train(SimState s, int me)
        {
            _train.Clear();
            int gold = s.Gold[me];
            int enemyTowers = 0;
            if (Consts.MaxTurns - s.Turn >= GiantMinLeft)
                for (int i = 0; i < s.Sites.Length; i++)
                    if (s.Sites[i].Structure == StructureType.Tower && s.Sites[i].Owner != me) enemyTowers++;
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (st.Structure != StructureType.Barracks || st.Owner != me || st.Training || st.CreepType != 2) continue;
                if (enemyTowers >= GiantWhenTowers && gold >= CreepStats.Cost[2]) { _train.Add(st.Id); gold -= CreepStats.Cost[2]; }
            }
            // копим на гиганта: пока у врага >= GiantWhenTowers башен и есть своя казарма гигантов, рыцарей тренируем только сверх 140 —
            // но только если 140 достижимы за GiantSaveTurns ходов при текущем доходе (иначе при иссякших шахтах бот сидел на 120 золота до конца партии)
            bool saveForGiant = false;
            if (enemyTowers >= GiantWhenTowers && gold < CreepStats.Cost[2] && s.Health[1 - me] > LowHpRush)
            {
                int income = 0;
                for (int i = 0; i < s.Sites.Length; i++)
                    if (s.Sites[i].Structure == StructureType.Mine && s.Sites[i].Owner == me) income += s.Sites[i].Rate;
                if (gold + income * GiantSaveTurns >= CreepStats.Cost[2])
                    for (int i = 0; i < s.Sites.Length; i++)
                    {
                        SimSite st = s.Sites[i];
                        if (st.Structure == StructureType.Barracks && st.Owner == me && st.CreepType == 2) saveForGiant = true;
                    }
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

        /// <summary>Добавляет в _near до k ближайших к королеве сайтов: свободных (free) или своих (иначе), начиная с позиции from; возвращает новый размер.</summary>
        private int Nearest(SimState s, int me, int from, int k, bool free)
        {
            int found = from, cap = Math.Min(from + k, _near.Length);
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (free ? st.Structure != StructureType.None : st.Owner != me) continue;
                double d = SimState.D2(st.X, st.Y, s.QueenX[me], s.QueenY[me]);
                int pos = found < cap ? found++ : -1;
                if (pos < 0)
                {
                    int worst = from;
                    for (int j = from + 1; j < cap; j++) if (_nearD[j] > _nearD[worst]) worst = j;
                    if (d >= _nearD[worst]) continue;
                    pos = worst;
                }
                _near[pos] = i; _nearD[pos] = d;
            }
            return found;
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
            int myTowers = 0;
            for (int i = 0; i < s.Sites.Length; i++) if (s.Sites[i].Owner == me && s.Sites[i].Structure == StructureType.Tower) myTowers++;
            bool knightsAlive = false;
            for (int i = 0; i < s.CreepCount[1 - me]; i++) if (s.Creeps[1 - me][i].Type == 0) { knightsAlive = true; break; }
            bool enemyKnightBarracks = false;
            for (int i = 0; i < s.Sites.Length; i++)
                if (s.Sites[i].Structure == StructureType.Barracks && s.Sites[i].Owner != me && s.Sites[i].CreepType == 0) { enemyKnightBarracks = true; break; }
            // фазы: экономика (доход < TargetIncome) — башни только по необходимости; дальше до MaxTowersCalm; при рыцарях без ограничений
            bool towersAllowed = knightsAlive
                || (income >= TargetIncome && myTowers < MaxTowersCalm)
                || (enemyKnightBarracks && myTowers < TowersEarly);

            // ближайшие свободные сайты (на чужие постройки строить нельзя — предупреждение рефери) и ближайшие свои
            // (прокачка шахты/башни, шахта -> башня при угрозе); раньше брались 4 ближайших любых сайта, и когда все они
            // были нашими, строить было нечего — бот уходил бродить
            int found = Nearest(s, me, 0, NearSites, true);
            found = Nearest(s, me, found, NearOwn, false);
            // ближайшие свободные сайты с золотом, даже далёкие: цель для похода за экономикой
            if (Rules.Mines)
            {
                for (int extra = 0; extra < FarMineSites && found < _near.Length; extra++)
                {
                    int best = -1; double bestD = double.MaxValue;
                    double hx = me == 0 ? 200 : Consts.WorldWidth - 200, hy = me == 0 ? 200 : Consts.WorldHeight - 200;
                    for (int i = 0; i < s.Sites.Length; i++)
                    {
                        SimSite st = s.Sites[i];
                        if (st.Structure != StructureType.None || st.Gold == 0) continue;
                        if (SimState.D2(st.X, st.Y, hx, hy) > SimState.D2(st.X, st.Y, Consts.WorldWidth - hx, Consts.WorldHeight - hy)) continue;
                        bool dup = false;
                        for (int j = 0; j < found; j++) if (_near[j] == i) { dup = true; break; }
                        if (dup) continue;
                        double d = SimState.D2(st.X, st.Y, s.QueenX[me], s.QueenY[me]);
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best < 0) break;
                    _near[found] = best; _nearD[found] = bestD; found++;
                }
            }

            for (int j = 0; j < found && n < buf.Length - 5; j++)
            {
                SimSite st = s.Sites[_near[j]];
                if (st.Structure == StructureType.None)
                {
                    if (Rules.Mines && st.Gold != 0 && !EnemyKnightNear(s, me, st.X, st.Y)) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Mine);
                    if (Rules.Towers && towersAllowed) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Tower);
                    if (j < BarracksSites && knightBarracks < wantKnightBarracks) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.BarracksKnight);
                    if (j < BarracksSites && Rules.Giants && giantBarracks == 0 && enemyTowers >= GiantWhenTowers) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.BarracksGiant);
                }
                else if (st.Structure == StructureType.Mine)
                {
                    if (st.MaxMineSize < 0 || st.Rate < st.MaxMineSize) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Mine);
                    if (Rules.Towers && knightsAlive) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Tower);   // шахта -> башня под ударом
                }
                else if (st.Structure == StructureType.Tower)
                {
                    if (st.Hp <= Consts.TowerHpMax - Consts.TowerHpIncrement && (st.Hp < UpgradeCalmBelow || knightsAlive)) buf[n++] = QueenAction.BuildAt(st.Id, BuildType.Tower);
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
