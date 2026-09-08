using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Royale.Tests
{
    /// <summary>
    /// Сверка симулятора с логами рефери (tools/referee/match.sh ... -log dir): на каждом ходу состояние собирается
    /// из ввода обоих игроков, применяются их ответы, и ожидаемый ввод следующего хода для каждого игрока
    /// сравнивается с логом. Токен "?" в ожидании — значение, которого симулятор не знает (золото далёкого сайта).
    /// </summary>
    public static class Replay
    {
        public sealed class Frame
        {
            public int Index, Player;
            public List<string> Input = new List<string>();
            public string Queen, Train;
            public List<string> Summary = new List<string>();
        }

        public sealed class GameLog
        {
            public string Path;
            public int Seed, League = 4;
            public List<string>[] Init = { new List<string>(), new List<string>() };
            public List<Frame> Frames = new List<Frame>();
        }

        public static string FixturesDir()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "tests", "fixtures");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return "tests/fixtures";
        }

        public static GameLog Parse(string path)
        {
            var g = new GameLog { Path = path };
            Frame cur = null;
            int initPlayer = -1;
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("# game "))
                {
                    foreach (string tok in line.Substring(7).Split(' '))
                    {
                        if (tok.StartsWith("seed=")) g.Seed = int.Parse(tok.Substring(5));
                        else if (tok.StartsWith("league=")) g.League = int.Parse(tok.Substring(7));
                    }
                    continue;
                }
                if (line.StartsWith("# init player ")) { initPlayer = int.Parse(line.Substring(14)); cur = null; continue; }
                if (line.StartsWith("# frame "))
                {
                    string[] t = line.Split(' ');
                    cur = new Frame { Index = int.Parse(t[2]), Player = int.Parse(t[4]) };
                    g.Frames.Add(cur);
                    initPlayer = -1;
                    continue;
                }
                if (line.StartsWith("# summary ")) { if (cur != null) cur.Summary.Add(line.Substring(10)); continue; }
                if (line.StartsWith("#")) continue;
                if (line.StartsWith("> "))
                {
                    if (cur != null) { if (cur.Queen == null) cur.Queen = line.Substring(2); else cur.Train = line.Substring(2); }
                    continue;
                }
                if (line.Length == 0) continue;
                if (initPlayer >= 0) g.Init[initPlayer].Add(line);
                else if (cur != null) cur.Input.Add(line);
            }
            return g;
        }

        public sealed class Stats
        {
            public int Games, Turns, BadTurns, BadLines, Skipped, Kills;
            public List<string> Examples = new List<string>();
        }

        public static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
        {
            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    var files = Directory.GetFiles(p, "*.log");
                    Array.Sort(files, StringComparer.Ordinal);
                    foreach (string f in files) yield return f;
                }
                else yield return p;
            }
        }

        public static Stats Check(IEnumerable<string> paths, int maxExamples)
        {
            var st = new Stats();
            foreach (string file in ExpandPaths(paths)) CheckGame(file, st, maxExamples);
            return st;
        }

        public static void CheckGame(string file, Stats st, int maxExamples)
        {
            GameLog g = Parse(file);
            int savedLeague = Rules.League;
            Rules.League = g.League;
            try
            {
                st.Games++;
                var init = new Site[2][];
                var mem = new SiteMemory[2];
                for (int p = 0; p < 2; p++)
                {
                    var r = new StringReader(string.Join("\n", g.Init[p].ToArray()) + "\n");
                    init[p] = InputParser.ReadInit(r.ReadLine(), r);
                    mem[p] = new SiteMemory();
                }
                var byTurn = new Dictionary<int, Frame[]>();
                foreach (Frame f in g.Frames)
                {
                    int t = f.Index / 2;
                    Frame[] pair;
                    if (!byTurn.TryGetValue(t, out pair)) { pair = new Frame[2]; byTurn[t] = pair; }
                    pair[f.Player] = f;
                }
                SimState carry = null;
                for (int t = 0; ; t++)
                {
                    Frame[] cur, next;
                    if (!byTurn.TryGetValue(t, out cur) || cur[0] == null || cur[1] == null) break;
                    if (cur[0].Queen == null || cur[1].Queen == null) break;
                    var inputs = new TurnInput[2];
                    for (int p = 0; p < 2; p++)
                    {
                        var r = new StringReader(string.Join("\n", cur[p].Input.ToArray()) + "\n");
                        inputs[p] = InputParser.ReadTurn(r.ReadLine(), r, init[p]);
                        mem[p].Apply(inputs[p]);
                    }
                    if (!byTurn.TryGetValue(t + 1, out next) || next[0] == null || next[1] == null) break;

                    SimState sim = SimState.FromInputs(inputs[0], inputs[1], t, carry);
                    SimAction a0 = SimAction.Parse(cur[0].Queen, cur[0].Train);
                    SimAction a1 = SimAction.Parse(cur[1].Queen, cur[1].Train);
                    sim.Step(a0, a1);
                    carry = sim;
                    st.Turns++;
                    if (sim.Killed[0] || sim.Killed[1]) st.Kills++;

                    bool badTurn = false;
                    for (int p = 0; p < 2; p++)
                    {
                        var expected = new List<string>();
                        sim.ToInputLines(p, expected);
                        List<string> actual = next[p].Input;
                        int n = Math.Max(expected.Count, actual.Count);
                        for (int i = 0; i < n; i++)
                        {
                            string e = i < expected.Count ? expected[i] : "<none>";
                            string a = i < actual.Count ? actual[i] : "<none>";
                            if (!LinesMatch(e, a, st))
                            {
                                st.BadLines++;
                                badTurn = true;
                                if (st.Examples.Count < maxExamples)
                                    st.Examples.Add(Path.GetFileName(file) + " turn " + t + " player " + p + " line " + i + ": expected [" + e + "] got [" + a + "]" +
                                        "  | actions p0 <" + cur[0].Queen + " ; " + cur[0].Train + "> p1 <" + cur[1].Queen + " ; " + cur[1].Train + ">");
                            }
                        }
                    }
                    if (badTurn) st.BadTurns++;
                }
            }
            finally { Rules.League = savedLeague; }
        }

        private static bool LinesMatch(string expected, string actual, Stats st)
        {
            if (expected == actual) return true;
            string[] e = expected.Split(' '), a = actual.Split(' ');
            if (e.Length != a.Length) return false;
            for (int i = 0; i < e.Length; i++)
            {
                if (e[i] == a[i]) continue;
                if (e[i] == "?") { st.Skipped++; continue; }
                return false;
            }
            return true;
        }

        public static int Run(string[] args)
        {
            int maxExamples = 20;
            var paths = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("dump=")) maxExamples = int.Parse(args[i].Substring(5));
                else paths.Add(args[i]);
            }
            if (paths.Count == 0) paths.Add(FixturesDir());
            Stats st = Check(paths, maxExamples);
            foreach (string ex in st.Examples) Console.WriteLine("  " + ex);
            Console.WriteLine("games " + st.Games + ", turns " + st.Turns + ", mismatched turns " + st.BadTurns + ", mismatched lines " + st.BadLines + ", skipped unknown fields " + st.Skipped + ", kills " + st.Kills);
            return st.BadLines == 0 ? 0 : 1;
        }
    }
}
