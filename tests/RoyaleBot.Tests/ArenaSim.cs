using System;
using System.Collections.Generic;
using System.IO;

namespace Royale.Tests
{
    /// <summary>
    /// Прогон партии арены через симулятор: карта из локального лога (тот же seed в рефери), действия обоих игроков
    /// из реплея (tools/arena: *.actions.txt), сравнение золота и HP по ходам с HUD арены (*.hud.txt).
    /// arenasim <лог> <actions.txt> <hud.txt> [order=old]
    /// </summary>
    public static class ArenaSim
    {
        public static int Run(string[] args)
        {
            string logPath = args[1], actionsPath = args[2], hudPath = args[3];
            for (int i = 4; i < args.Length; i++) if (args[i] == "order=old") SimState.InterleavedQueens = true;
            Rules.League = 4;
            Replay.GameLog g = Replay.Parse(logPath);
            var init = new Site[2][];
            for (int p = 0; p < 2; p++)
            {
                var r = new StringReader(string.Join("\n", g.Init[p].ToArray()) + "\n");
                init[p] = InputParser.ReadInit(r.ReadLine(), r);
            }
            Replay.Frame f0 = null, f1 = null;
            foreach (Replay.Frame f in g.Frames) { if (f.Index / 2 == 0) { if (f.Player == 0) f0 = f; else f1 = f; } }
            var inputs = new TurnInput[2];
            for (int p = 0; p < 2; p++)
            {
                Replay.Frame f = p == 0 ? f0 : f1;
                var r = new StringReader(string.Join("\n", f.Input.ToArray()) + "\n");
                inputs[p] = InputParser.ReadTurn(r.ReadLine(), r, init[p]);
            }
            SimState s = SimState.FromInputs(inputs[0], inputs[1], 0);

            var actions = new Dictionary<int, SimAction[]>();
            foreach (string line in File.ReadLines(actionsPath))
            {
                string[] t = line.Split(new[] { ' ' }, 3);
                int turn = int.Parse(t[0]), p = int.Parse(t[1]);
                string[] qt = t[2].Split(new[] { " | " }, StringSplitOptions.None);
                SimAction[] pair;
                if (!actions.TryGetValue(turn, out pair)) { pair = new SimAction[2]; actions[turn] = pair; }
                pair[p] = SimAction.Parse(qt[0], qt.Length > 1 ? qt[1] : "TRAIN");
            }
            var hud = new Dictionary<int, string[]>();
            foreach (string line in File.ReadLines(hudPath))
            {
                string[] t = line.Split(' ');
                hud[int.Parse(t[0])] = t;
            }
            int matched = 0, firstDiff = -1;
            for (int turn = 0; ; turn++)
            {
                SimAction[] pair;
                if (!actions.TryGetValue(turn, out pair) || s.GameOver) break;
                s.Step(pair[0], pair[1]);
                string[] h;
                if (!hud.TryGetValue(turn, out h)) continue;
                string mine = s.Gold[0] + " " + s.Health[0] + " " + s.Gold[1] + " " + s.Health[1];
                string arena = h[1] + " " + h[2] + " " + h[3] + " " + h[4];
                if (mine == arena) matched++;
                else
                {
                    if (firstDiff < 0) firstDiff = turn;
                    if (turn - firstDiff < 4) Console.WriteLine("turn " + turn + ": sim " + mine + " vs arena " + arena);
                }
            }
            Console.WriteLine((SimState.InterleavedQueens ? "order=old" : "order=new") + ": matched turns " + matched + ", first diff " + firstDiff);
            return firstDiff < 0 ? 0 : 1;
        }
    }
}
