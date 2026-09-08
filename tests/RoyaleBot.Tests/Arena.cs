using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Royale.Tests
{
    /// <summary>
    /// Арена self-play в процессе: симулятор как арбитр, карта как в MapBuilding.kt (случайная, не бит-в-бит с Java),
    /// две стратегии по key=value (как в Program: wood=1 — WoodStrategy, иначе SearchStrategy), бюджет хода ms.
    /// arena games=N seed=S ms=6 threads=4 a="key=value ..." b="key=value ..." [quiet=1]
    /// Стороны чередуются; печатает счёт A:B:ничьи и строку "total a=W b=L d=D".
    /// </summary>
    public static class Arena
    {
        public sealed class Result { public int A, B, D; public long Turns; }

        public static int Run(string[] args)
        {
            int games = 40, seed = 1, ms = 6, threads = 4;
            bool quiet = false;
            string a = "", b = "";
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("games=")) games = int.Parse(arg.Substring(6));
                else if (arg.StartsWith("seed=")) seed = int.Parse(arg.Substring(5));
                else if (arg.StartsWith("ms=")) ms = int.Parse(arg.Substring(3));
                else if (arg.StartsWith("threads=")) threads = int.Parse(arg.Substring(8));
                else if (arg.StartsWith("a=")) a = arg.Substring(2);
                else if (arg.StartsWith("b=")) b = arg.Substring(2);
                else if (arg == "quiet=1") quiet = true;
            }
            Rules.League = 4;
            Result r = Play(games, seed, ms, threads, a, b, quiet);
            Console.WriteLine("total a=" + r.A + " b=" + r.B + " d=" + r.D + " games=" + games + " avgturns=" + (games > 0 ? r.Turns / games : 0));
            return 0;
        }

        public static Result Play(int games, int seed, int ms, int threads, string a, string b, bool quiet)
        {
            var res = new Result();
            var opts = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) };
            Parallel.For(0, games, opts, g =>
            {
                bool swapped = g % 2 == 1;
                IStrategy s0 = Make(swapped ? b : a), s1 = Make(swapped ? a : b);
                int turns;
                int winner = PlayGame(seed + g, s0, s1, ms, out turns);   // 0/1/-1 в терминах абсолютных игроков
                int winA = winner < 0 ? -1 : (winner == (swapped ? 1 : 0) ? 1 : 0);
                lock (res)
                {
                    if (winA == 1) res.A++; else if (winA == 0) res.B++; else res.D++;
                    res.Turns += turns;
                    if (!quiet) Console.WriteLine("game " + g + " seed " + (seed + g) + ": " + (winA == 1 ? "A" : winA == 0 ? "B" : "draw") + " in " + turns + " turns" + (swapped ? " (swapped)" : ""));
                }
            });
            return res;
        }

        public static IStrategy Make(string spec)
        {
            string[] args = spec.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var wood = new WoodStrategy();
            var search = new SearchStrategy(null);
            Tuning.Apply(args, wood, search, TextWriter.Null);
            foreach (string x in args) if (x == "wood=1") return wood;
            foreach (string x in args) if (x.StartsWith("random=")) return new RandomStrategy(int.Parse(x.Substring(7)));
            return search;
        }

        /// <summary>Партия на симуляторе; возвращает победителя (0/1) или -1 (ничья).</summary>
        public static int PlayGame(int seed, IStrategy s0, IStrategy s1, int ms, out int turns)
        {
            var rnd = new Random(seed);
            SimState state = NewGame(rnd);
            var init = new Site[2][];
            var mem = new SiteMemory[] { new SiteMemory(), new SiteMemory() };
            for (int p = 0; p < 2; p++)
            {
                init[p] = new Site[state.Sites.Length];
                for (int i = 0; i < state.Sites.Length; i++)
                {
                    SimSite st = state.Sites[i];
                    init[p][i] = new Site { Id = st.Id, X = st.X, Y = st.Y, Radius = st.Radius, Gold = -1, MaxMineSize = -1, KnownGold = -1, KnownMaxMineSize = -1, Structure = StructureType.None, Owner = -1, Param1 = -1, Param2 = -1 };
                }
            }
            var lines = new List<string>();
            var actions = new SimAction[2];
            turns = 0;
            while (!state.GameOver)
            {
                for (int p = 0; p < 2; p++)
                {
                    lines.Clear();
                    state.ToInputLines(p, lines);
                    for (int i = 0; i < lines.Count; i++) lines[i] = lines[i].Replace("?", "-1");
                    var reader = new StringReader(string.Join("\n", lines.ToArray()) + "\n");
                    TurnInput t = InputParser.ReadTurn(reader.ReadLine(), reader, init[p]);
                    mem[p].Apply(t);
                    var clock = new TurnClock(turns == 0 ? Math.Max(ms, 50) : ms);
                    TurnOutput o;
                    try { o = (p == 0 ? s0 : s1).Play(t, clock); }
                    catch (Exception) { o = TurnOutput.Idle; }
                    actions[p] = SimAction.Parse(o.Queen.Format(), o.TrainLine());
                }
                state.Step(actions[0], actions[1]);
                turns++;
            }
            return state.Winner;
        }

        /// <summary>Карта как в MapBuilding.kt для лиги Bronze+: 12 пар сайтов, центральная симметрия, зазор 90.</summary>
        public static SimState NewGame(Random rnd)
        {
            const int pairs = 12, W = Consts.WorldWidth, H = Consts.WorldHeight;
            int n = pairs * 2;
            var x = new double[n]; var y = new double[n]; var r = new int[n]; var gold = new int[n]; var size = new int[n];
            for (int i = 0; i < pairs; i++)
            {
                int rate = rnd.Next(1, 4), g = rnd.Next(200, 251), rad = rnd.Next(60, 91);
                double px = rnd.Next(W), py = rnd.Next(H);
                x[2 * i] = px; y[2 * i] = py; x[2 * i + 1] = W - px; y[2 * i + 1] = H - py;
                r[2 * i] = r[2 * i + 1] = rad; gold[2 * i] = gold[2 * i + 1] = g; size[2 * i] = size[2 * i + 1] = rate;
            }
            for (int iter = 0; iter < 100; iter++)
            {
                for (int i = 0; i < pairs; i++)
                {
                    double mx = (x[2 * i] + (W - x[2 * i + 1])) / 2, my = (y[2 * i] + (H - y[2 * i + 1])) / 2;
                    x[2 * i] = mx; y[2 * i] = my; x[2 * i + 1] = W - mx; y[2 * i + 1] = H - my;
                }
                if (!ObstaclePass(x, y, r, 90.0)) break;
            }
            var s = new SimState();
            s.Turn = 0;
            s.Sites = new SimSite[n];
            for (int i = 0; i < n; i++)
            {
                int sx = JavaMath.Round(x[i]), sy = JavaMath.Round(y[i]);
                var st = new SimSite { Id = i, X = sx, Y = sy, Radius = r[i], Area = Math.PI * r[i] * r[i], Gold = gold[i], MaxMineSize = size[i], Structure = StructureType.None, Owner = -1 };
                double dc = Math.Sqrt(SimState.D2(sx, sy, W / 2, H / 2));
                if (dc < 500) { st.MaxMineSize++; st.Gold += 50; }
                if (dc < 200) { st.MaxMineSize++; st.Gold += 50; }
                s.Sites[i] = st;
            }
            s.Gold[0] = s.Gold[1] = Consts.StartingGold;
            int hp = rnd.Next(5, 21) * 5;
            s.Health[0] = s.Health[1] = hp;
            s.QueenX[0] = 200; s.QueenY[0] = 200; s.QueenX[1] = W - 200; s.QueenY[1] = H - 200;
            s.RestoreInitialQueens();
            return s;
        }

        /// <summary>collisionCheck для сайтов (все с массой 0): расталкивание с зазором gap; true — было исправление.</summary>
        private static bool ObstaclePass(double[] x, double[] y, int[] r, double gap)
        {
            int n = x.Length;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                double clamp = 90 + r[i];
                x[i] = Math.Max(clamp, Math.Min(Consts.WorldWidth - clamp, x[i]));
                y[i] = Math.Max(clamp, Math.Min(Consts.WorldHeight - clamp, y[i]));
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    double dx = x[j] - x[i], dy = y[j] - y[i];
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double overlap = r[i] + r[j] + gap - dist;
                    if (overlap <= 1e-6) continue;
                    double rx, ry;
                    SimState.Resized(dx, dy, 0.5 * overlap + 20.0, out rx, out ry);
                    x[i] -= rx; y[i] -= ry; x[j] += rx; y[j] += ry;
                    any = true;
                }
            }
            return any;
        }
    }
}
