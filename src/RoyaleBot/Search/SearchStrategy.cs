using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace Royale
{
    /// <summary>
    /// Beam search по последовательностям действий королевы на симуляторе. Противник: королева стоит,
    /// ничего не тренирует; его казармы дотренировывают, башни стреляют. Мой TRAIN на каждом шаге — Macro.
    /// Оценка листа — Eval. Ответ — первое действие лучшей цепочки последнего завершённого уровня.
    /// </summary>
    public sealed class SearchStrategy : IStrategy
    {
        public int Depth = 12;
        public int Width = 16;
        public int FineDepth = 4;       // до этой глубины — все кандидаты, дальше только «то же действие» или WAIT
        public int MaxMs = 15;          // бюджет поиска на обычном ходу вместе с продолжениями (на CodinGame таймауты: 2 из 60 партий при 25, 1 из 59 при 18; глубина 12 обычно укладывается в 9)
        public int RolloutTurns = 8;    // продолжение лучших листьев: повторять последнее действие ещё столько ходов
        public int RolloutLeaves = 6;   // сколько лучших листьев продолжать (резерв времени = их шаги по замеренной цене, но не больше трети бюджета)
        public double RolloutWeight = 0.7;  // доля оценки после продолжения в итоговой оценке листа
        public bool Debug;              // печатать кандидатов корня с оценками (ключ debug=1)
        public double Persist = 240;    // премия цепочке, начинающейся с прошлого действия (против метаний между планами)
        public int MinWaveDamage = 1;   // рыцарей тренируем, только если волна (все свободные казармы) в симуляции с неподвижными королевами снимает с чужой королевы не меньше HP (0 — не проверять)
        public int WaveTurns = 40;      // горизонт этой симуляции
        public int LastWaveDamage = -1;  // урон чужой королеве в последней проверке волны (для отладки)
        private QueenAction _lastAction;
        private bool _hasLast;
        public int FirstTurnMs = 300;   // на первом ходу (лимит 1000 мс, JIT)
        public readonly EvalWeights W = new EvalWeights();
        public readonly Macro Macro = new Macro();

        private sealed class Node
        {
            public readonly SimState State = new SimState();
            public double Score;
            public QueenAction First, Last;
        }

        private readonly Random _rnd = new Random(12345);
        private readonly SimState _scratch = new SimState();
        private readonly QueenAction[] _cand = new QueenAction[48];
        private Node[] _beam, _bankA, _bankB;
        private int[] _order;
        private double[] _keys;
        private int _turn;
        private readonly TextWriter _log;
        private int _enemyGold = Consts.StartingGold;
        private int[] _enemyBarracksBusy;   // param1 чужих казарм на прошлом ходу (для счётчика его золота)
        public string LastInfo = "";

        public SearchStrategy() : this(null) { }
        public SearchStrategy(TextWriter log) { _log = log; }

        private void Allocate()
        {
            int cap = Width * _cand.Length;
            if (_bankA != null && _bankA.Length >= cap) return;
            _bankA = new Node[cap]; _bankB = new Node[cap];
            for (int i = 0; i < cap; i++) { _bankA[i] = new Node(); _bankB[i] = new Node(); }
            _beam = new Node[Width];
            _order = new int[cap];
            _keys = new double[cap];
        }

        public void WarmUp(TurnClock clock) { }

        public TurnOutput Play(TurnInput t, TurnClock clock)
        {
            Allocate();
            SimState root = SimState.FromInputs(t, null, _turn);
            int me = root.MyIndex;
            UpdateEnemyGold(t);
            root.Gold[1 - me] = _enemyGold;
            long deadline = Math.Min(clock.BudgetMs - 2, _turn == 0 ? FirstTurnMs : MaxMs);
            _turn++;

            QueenAction best = Search(root, me, clock, deadline);
            // TRAIN считается по корню после поиска: Macro.Train отдаёт общий буфер, который поиск перезаписывает
            // состояниями будущих ходов (была команда TRAIN занятой казарме / без золота = предупреждение и потерянный ход)
            int[] train = (int[])Macro.Train(root, me).Clone();
            if (MinWaveDamage > 0 && train.Length > 0 && root.Health[1 - me] > Macro.LowHpRush) train = FilterWave(root, me, train);
            return new TurnOutput { Queen = best, Train = train };
        }

        /// <summary>
        /// Волна рыцарей, которая целиком гибнет о башни, — выброшенное золото (4 рыцаря против черепахи).
        /// Симулируем тренировку и WaveTurns ходов с неподвижными королевами без новых тренировок; если чужая королева
        /// теряет меньше MinWaveDamage HP, рыцарей из списка убираем (гигантов оставляем).
        /// </summary>
        private int[] FilterWave(SimState root, int me, int[] train)
        {
            int knights = 0;
            for (int i = 0; i < train.Length; i++)
            {
                SimSite st = root.Sites[root.SiteIndex(train[i])];
                if (st.CreepType == 0) knights++;
            }
            if (knights == 0) return train;
            int e = 1 - me;
            _scratch.CopyFrom(root);
            var mine = new SimAction { Queen = QueenAction.Wait(), Train = train, BuildTypeValid = true };
            SimAction enemy = SimAction.Wait();
            if (me == 0) _scratch.Step(mine, enemy); else _scratch.Step(enemy, mine);
            SimAction none = SimAction.Wait();
            for (int t = 1; t < WaveTurns && !_scratch.GameOver; t++)
            {
                _scratch.Step(none, none);
                if (t > CreepStats.BuildTime[0] && _scratch.CreepCount[me] == 0) break;
            }
            int damage = root.Health[e] - _scratch.Health[e];
            LastWaveDamage = damage;
            if (damage >= MinWaveDamage) return train;
            if (knights == train.Length) return SimAction.NoTrain;
            var keep = new int[train.Length - knights];
            int n = 0;
            for (int i = 0; i < train.Length; i++) if (root.Sites[root.SiteIndex(train[i])].CreepType != 0) keep[n++] = train[i];
            return keep;
        }

        /// <summary>
        /// Оценка золота противника: старт 100, плюс доход его шахт (невидимый доход — максимум сайта или 2),
        /// минус стоимость тренировок, которые он начал (казарма перешла из простоя в тренировку).
        /// </summary>
        private void UpdateEnemyGold(TurnInput t)
        {
            if (_enemyBarracksBusy == null || _enemyBarracksBusy.Length != t.Sites.Length)
            {
                _enemyBarracksBusy = new int[t.Sites.Length];
                _enemyGold = Consts.StartingGold;
            }
            int income = 0;
            for (int i = 0; i < t.Sites.Length; i++)
            {
                Site st = t.Sites[i];
                if (st.IsEnemy && st.Structure == StructureType.Mine) income += st.Param1 > 0 ? st.Param1 : Math.Max(1, SimState.AssumedMineRate(st.KnownMaxMineSize));
                int busy = st.IsEnemy && st.Structure == StructureType.Barracks ? st.Param1 : 0;
                if (busy > 0 && _enemyBarracksBusy[i] == 0 && st.Param2 >= 0 && st.Param2 < 3) _enemyGold -= CreepStats.Cost[st.Param2];
                _enemyBarracksBusy[i] = busy;
            }
            if (_turn > 0) _enemyGold += Rules.FixedIncome ? Consts.WoodFixedIncome : income;
            if (_enemyGold < 0) _enemyGold = 0;
        }

        private QueenAction Search(SimState root, int me, TurnClock clock, long deadline)
        {
            SimAction enemy = SimAction.Wait();
            Node[] bank = _bankA;
            int beamCount = 0;
            QueenAction bestFirst = QueenAction.Wait();
            double bestScore = double.NegativeInfinity;
            int depthDone = 0, expanded = 0;

            // уровень 0: из корня
            int filled = Expand(root, me, enemy, bank, 0, true, clock, deadline, ref expanded, out bool _);
            if (filled == 0) return bestFirst;
            if (Debug && _log != null)
            {
                _log.WriteLine("root: gold " + root.Gold[me] + " enemy gold " + root.Gold[1 - me] + " turn " + root.Turn + " me " + me + " queen " + root.QueenX[me] + "," + root.QueenY[me]);
                for (int i = 0; i < filled; i++)
                {
                    _scratch.CopyFrom(bank[i].State);
                    for (int r = 0; r < RolloutTurns && !_scratch.GameOver; r++)
                    {
                        var mine = new SimAction { Queen = bank[i].Last, Train = Macro.Train(_scratch, me), BuildTypeValid = true };
                        enemy.Train = Macro.Train(_scratch, 1 - me);
                        if (me == 0) _scratch.Step(mine, enemy); else _scratch.Step(enemy, mine);
                    }
                    _log.WriteLine("  cand " + bank[i].Last.Format().PadRight(24) + " static " + bank[i].Score.ToString("F0").PadLeft(7) + " rollout " + Eval.Score(_scratch, me, W, _rnd).ToString("F0").PadLeft(7) + " hp " + _scratch.Health[me] + " towers " + CountOwn(_scratch, me, StructureType.Tower) + " mines " + CountOwn(_scratch, me, StructureType.Mine));
                }
            }
            beamCount = Select(bank, filled);
            bestFirst = _beam[0].First; bestScore = _beam[0].Score; depthDone = 1;

            // резерв времени под продолжения листьев: по цене шага на этом ходу
            long searchDeadline = deadline;
            for (int d = 1; d < Depth; d++)
            {
                if (RolloutTurns > 0 && expanded > 0)
                {
                    double stepMs = (double)clock.ElapsedMs / expanded;
                    long reserve = (long)Math.Ceiling(stepMs * Math.Min(beamCount, RolloutLeaves) * RolloutTurns * 1.3) + 1;
                    long cap = Math.Max(2, (deadline - clock.ElapsedMs) / 3);
                    searchDeadline = deadline - Math.Min(reserve, cap);
                }
                Node[] next = bank == _bankA ? _bankB : _bankA;
                int n = 0;
                bool timeUp = false;
                for (int b = 0; b < beamCount && !timeUp; b++)
                {
                    Node parent = _beam[b];
                    if (parent.State.GameOver) { CopyNode(parent, next[n++]); continue; }
                    n += Expand(parent.State, me, enemy, next, n, false, clock, searchDeadline, ref expanded, out timeUp, parent.First, d, parent.Last);
                }
                if (timeUp || n == 0) break;
                beamCount = Select(next, n);
                bank = next;
                bestFirst = _beam[0].First; bestScore = _beam[0].Score; depthDone = d + 1;
            }
            // Продолжение лучших листьев: повторяем последнее действие ещё RolloutTurns ходов (постройка
            // продолжает качаться, движение продолжается) — так видно, окупается ли башня, когда рыцари дойдут.
            if (RolloutTurns > 0 && beamCount > 0)
            {
                double bestMix = double.NegativeInfinity;
                int rolled = 0;
                for (int b = 0; b < Math.Min(beamCount, RolloutLeaves); b++)
                {
                    Node leaf = _beam[b];
                    double mix = leaf.Score;
                    if (!leaf.State.GameOver && clock.ElapsedMs < deadline)
                    {
                        _scratch.CopyFrom(leaf.State);
                        for (int r = 0; r < RolloutTurns && !_scratch.GameOver && clock.ElapsedMs < deadline; r++)
                        {
                            var mine = new SimAction { Queen = leaf.Last, Train = Macro.Train(_scratch, me), BuildTypeValid = true };
                            enemy.Train = Macro.Train(_scratch, 1 - me);
                            if (me == 0) _scratch.Step(mine, enemy); else _scratch.Step(enemy, mine);
                        }
                        double after = Eval.Score(_scratch, me, W, _rnd);
                        mix = (1 - RolloutWeight) * leaf.Score + RolloutWeight * after;
                        rolled++;
                    }
                    if (_hasLast && SameAction(leaf.First, _lastAction)) mix += Persist;
                    if (mix > bestMix) { bestMix = mix; bestFirst = leaf.First; bestScore = mix; }
                }
                expanded += rolled * RolloutTurns;
            }
            _lastAction = bestFirst; _hasLast = true;
            LastInfo = "depth " + depthDone + " nodes " + expanded + " score " + bestScore.ToString("F0") + " " + clock.ElapsedMs + " ms";
            if (_log != null) _log.WriteLine(LastInfo);
            return bestFirst;
        }

        private static bool SameAction(QueenAction a, QueenAction b)
        {
            if (a.Kind != b.Kind) return false;
            if (a.Kind == QueenActionKind.Build) return a.SiteId == b.SiteId && a.Build == b.Build;
            if (a.Kind == QueenActionKind.Move) return a.X == b.X && a.Y == b.Y;
            return true;
        }

        private static int CountOwn(SimState s, int me, StructureType t)
        {
            int n = 0;
            for (int i = 0; i < s.Sites.Length; i++) if (s.Sites[i].Owner == me && s.Sites[i].Structure == t) n++;
            return n;
        }

        private static void CopyNode(Node from, Node to)
        {
            to.State.CopyFrom(from.State);
            to.Score = from.Score;
            to.First = from.First;
        }

        /// <summary>Раскрывает state всеми кандидатами в bank начиная с offset; возвращает число добавленных.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private int Expand(SimState state, int me, SimAction enemy, Node[] bank, int offset, bool isRoot, TurnClock clock, long deadline, ref int expanded, out bool timeUp, QueenAction first = default(QueenAction), int depth = 0, QueenAction last = default(QueenAction))
        {
            timeUp = false;
            int nc;
            if (depth < FineDepth) nc = Macro.Candidates(state, me, _cand);
            else
            {
                _cand[0] = last;
                nc = 1;
                if (last.Kind != QueenActionKind.Wait) _cand[nc++] = QueenAction.Wait();
            }
            int[] train = Macro.Train(state, me);
            enemy.Train = Macro.Train(state, 1 - me);
            int n = 0;
            for (int i = 0; i < nc; i++)
            {
                if (!isRoot && (expanded & 3) == 0 && clock.ElapsedMs >= deadline) { timeUp = true; break; }
                Node child = bank[offset + n];
                child.State.CopyFrom(state);
                var mine = new SimAction { Queen = _cand[i], Train = train, BuildTypeValid = true };
                if (me == 0) child.State.Step(mine, enemy); else child.State.Step(enemy, mine);
                child.Score = Eval.Score(child.State, me, W, _rnd);
                child.First = isRoot ? _cand[i] : first;
                child.Last = _cand[i];
                n++;
                expanded++;
            }
            return n;
        }

        private readonly HashSet<long> _seen = new HashSet<long>(1024);

        /// <summary>Лучшие Width узлов bank[0..n) → _beam (по убыванию оценки), без дублей по положению королевы и постройкам.</summary>
        private int Select(Node[] bank, int n)
        {
            for (int i = 0; i < n; i++) { _order[i] = i; _keys[i] = -bank[i].Score; }
            Array.Sort(_keys, _order, 0, n);
            _seen.Clear();
            int count = 0;
            for (int i = 0; i < n && count < Width; i++)
            {
                Node node = bank[_order[i]];
                if (!_seen.Add(Key(node.State))) continue;
                _beam[count++] = node;
            }
            return count;
        }

        private static long Key(SimState s)
        {
            int me = s.MyIndex;
            long k = ((long)(s.QueenX[me] / 8) * 1000 + (long)(s.QueenY[me] / 8)) * 1000003L;
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (st.Owner != me || st.Structure == StructureType.None) continue;
                k = k * 31 + i * 7 + (int)st.Structure * 3 + st.Rate + st.Hp / 100 + (st.Training ? 1 : 0);
            }
            return k * 131 + s.Gold[me];
        }
    }
}
