using System;
using System.IO;

namespace Royale
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Буферизованный вывод: две строки и один Flush на ход.
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
            var stderr = Console.Error;

            var strategy = new WoodStrategy();
            Tuning.Apply(args, strategy, stderr);
            stderr.WriteLine("league " + Rules.League + " (" + Rules.Name + ")");
            var bot = new Bot(strategy, stderr);
            bot.Run(Console.In, stdout);
        }
    }

    /// <summary>Аргументы key=value для локальных матчей (на CodinGame аргументов нет).</summary>
    public static class Tuning
    {
        public static void Apply(string[] args, WoodStrategy s, TextWriter log)
        {
            foreach (string a in args)
            {
                int eq = a.IndexOf('=');
                if (eq <= 0) continue;
                string key = a.Substring(0, eq);
                int v;
                if (!int.TryParse(a.Substring(eq + 1), out v)) { log.WriteLine("bad value: " + a); continue; }
                switch (key)
                {
                    case "league": Rules.League = v; break;
                    case "income": s.TargetIncome = v; break;
                    case "towers": s.TargetTowers = v; break;
                    case "upgrade": s.TowerUpgradeBelow = v; break;
                    case "danger": s.DangerRadius = v; break;
                    default: log.WriteLine("unknown key: " + key); break;
                }
            }
        }
    }
}
