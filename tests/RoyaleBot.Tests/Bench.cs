using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Royale.Tests
{
    /// <summary>Скорость симулятора: bench [логи] [turn=60] [steps=5000] — шагов в мс из состояния хода turn.</summary>
    public static class Bench
    {
        public static int Run(string[] args)
        {
            int turn = 60, steps = 5000;
            var paths = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].StartsWith("turn=")) turn = int.Parse(args[i].Substring(5));
                else if (args[i].StartsWith("steps=")) steps = int.Parse(args[i].Substring(6));
                else if (args[i].StartsWith("iters=")) SimState.SubstepIterations = int.Parse(args[i].Substring(6));   // 0 — без расталкивания (оценка его доли)
                else if (args[i] == "profile=1") SimState.Profile = true;
                else paths.Add(args[i]);
            }
            if (paths.Count == 0) paths.Add(Replay.FixturesDir());
            double totalStepsPerMs = 0; int n = 0;
            foreach (string file in Replay.ExpandPaths(paths))
            {
                Replay.GameLog g = Replay.Parse(file);
                Rules.League = g.League;
                var init = new Site[2][];
                for (int p = 0; p < 2; p++)
                {
                    var r = new StringReader(string.Join("\n", g.Init[p].ToArray()) + "\n");
                    init[p] = InputParser.ReadInit(r.ReadLine(), r);
                }
                Replay.Frame f0 = null, f1 = null;
                foreach (Replay.Frame f in g.Frames) { if (f.Index / 2 == turn) { if (f.Player == 0) f0 = f; else f1 = f; } }
                if (f0 == null || f1 == null) continue;
                var inputs = new TurnInput[2];
                for (int p = 0; p < 2; p++)
                {
                    Replay.Frame f = p == 0 ? f0 : f1;
                    var r = new StringReader(string.Join("\n", f.Input.ToArray()) + "\n");
                    inputs[p] = InputParser.ReadTurn(r.ReadLine(), r, init[p]);
                }
                SimState s = SimState.FromInputs(inputs[0], inputs[1], turn);
                var work = new SimState();
                SimAction wait = SimAction.Wait();
                work.CopyFrom(s); work.Step(wait, wait);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < steps; i++) work.CopyFrom(s);
                double msCopy = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                for (int i = 0; i < steps; i++) { work.CopyFrom(s); work.Step(wait, wait); }
                double ms = sw.Elapsed.TotalMilliseconds;
                if (SimState.Profile)
                {
                    double f = 1000000.0 / Stopwatch.Frequency / steps;   // мкс на шаг
                    Console.WriteLine("  profile us/step: actions " + (SimState.ProfActions * f).ToString("F2") + ", creeps " + (SimState.ProfCreeps * f).ToString("F2") + " (move " + (SimState.ProfMove * f).ToString("F2") + ", collide " + (SimState.ProfCollide * f).ToString("F2") + " [load " + (SimState.ProfLoad * f).ToString("F2") + " build " + (SimState.ProfBuild * f).ToString("F2") + " pass " + (SimState.ProfPass * f).ToString("F2") + " store " + (SimState.ProfStore * f).ToString("F2") + "], damage " + (SimState.ProfDamage * f).ToString("F2") + ", tail " + (SimState.ProfTail * f).ToString("F2") + "), sites " + (SimState.ProfSites * f).ToString("F2") + ", end " + (SimState.ProfEnd * f).ToString("F2"));
                    SimState.ProfActions = SimState.ProfCreeps = SimState.ProfSites = SimState.ProfEnd = SimState.ProfMove = SimState.ProfCollide = SimState.ProfDamage = SimState.ProfTail = SimState.ProfLoad = SimState.ProfBuild = SimState.ProfPass = SimState.ProfStore = 0;
                }
                sw.Restart();
                int rollouts = Math.Max(1, steps / 15);
                for (int i = 0; i < rollouts; i++) { work.CopyFrom(s); for (int d = 0; d < 15; d++) work.Step(wait, wait); }
                double ms2 = sw.Elapsed.TotalMilliseconds;
                int units = s.CreepCount[0] + s.CreepCount[1];
                Console.WriteLine(Path.GetFileName(file) + ": turn " + turn + ", creeps " + units + ", " + (steps / ms).ToString("F1") + " steps/ms (copy+step), copy only " + (steps / msCopy).ToString("F0") + "/ms, rollout 15: " + (rollouts / ms2 * 1000).ToString("F0") + "/s");
                totalStepsPerMs += steps / ms; n++;
            }
            if (n > 0) Console.WriteLine("avg " + (totalStepsPerMs / n).ToString("F1") + " steps/ms");
            return 0;
        }
    }
}
