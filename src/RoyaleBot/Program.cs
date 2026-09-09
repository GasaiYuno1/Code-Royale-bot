using System;
using System.IO;

namespace Royale
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Буферизованный вывод: две строки и один Flush на ход.
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
            var stderr = Console.Error;

            var wood = new WoodStrategy();
            var search = new SearchStrategy(stderr);
            bool useWood = false;
            foreach (string a in args) if (a == "wood=1") useWood = true;
            Tuning.Apply(args, wood, search, stderr);
            stderr.WriteLine("league " + Rules.League + " (" + Rules.Name + ") " + (useWood ? "wood" : "search"));
            IStrategy chosen = useWood ? (IStrategy)wood : search;
            foreach (string a in args) if (a.StartsWith("random=")) chosen = new RandomStrategy(int.Parse(a.Substring(7)));
            var bot = new Bot(chosen, stderr);
            bot.Run(Console.In, stdout);
        }
    }

    /// <summary>Аргументы key=value для локальных матчей (на CodinGame аргументов нет).</summary>
    public static class Tuning
    {
        public static void Apply(string[] args, WoodStrategy w, SearchStrategy s, TextWriter log)
        {
            foreach (string a in args)
            {
                int eq = a.IndexOf('=');
                if (eq <= 0) continue;
                string key = a.Substring(0, eq);
                double v;
                if (!double.TryParse(a.Substring(eq + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) { log.WriteLine("bad value: " + a); continue; }
                int iv = (int)v;
                switch (key)
                {
                    case "league": Rules.League = iv; break;
                    case "wood": case "random": break;
                    // WoodStrategy
                    case "income": w.TargetIncome = iv; break;
                    case "towers": w.TargetTowers = iv; break;
                    case "upgrade": w.TowerUpgradeBelow = iv; break;
                    case "danger": w.DangerRadius = iv; break;
                    case "wreach": w.TowerReach = iv; break;
                    case "barlate": w.BarracksLate = iv != 0; break;
                    // SearchStrategy
                    case "depth": s.Depth = iv; break;
                    case "width": s.Width = iv; break;
                    case "ms": s.MaxMs = iv; break;
                    case "firstms": s.FirstTurnMs = iv; break;
                    case "rollout": s.RolloutTurns = iv; break;
                    case "leaves": s.RolloutLeaves = iv; break;
                    case "fine": s.FineDepth = iv; break;
                    case "persist": s.Persist = v; break;
                    case "debug": s.Debug = iv != 0; break;
                    case "rolloutw": s.RolloutWeight = v; break;
                    case "sites": s.Macro.NearSites = iv; break;
                    case "giants": s.Macro.GiantWhenTowers = iv; break;
                    case "bar2": s.Macro.SecondBarracksIncome = iv; break;
                    case "maxtowers": s.Macro.MaxTowersCalm = iv; break;
                    case "econ": s.Macro.TargetIncome = iv; break;
                    case "early": s.Macro.TowersEarly = iv; break;
                    case "upcalm": s.Macro.UpgradeCalmBelow = iv; break;
                    case "farmines": s.Macro.FarMineSites = iv; break;
                    default:
                        if (!s.W.Set(key, v)) log.WriteLine("unknown key: " + key);
                        break;
                }
            }
        }
    }
}
