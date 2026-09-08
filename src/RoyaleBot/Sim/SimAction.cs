using System;
using System.Collections.Generic;

namespace Royale
{
    /// <summary>
    /// Действие игрока за ход в терминах рефери: команда королевы и список казарм TRAIN.
    /// Разбор повторяет Referee.processPlayerActions: лишний токен, нецелое число, незнакомая команда — Invalid
    /// (рефери убивает игрока). Неверный тип постройки проверяется только при касании сайта (BuildTypeValid).
    /// </summary>
    public struct SimAction
    {
        public QueenAction Queen;
        public int[] Train;
        public bool Invalid;
        public bool BuildTypeValid;

        public static readonly int[] NoTrain = new int[0];

        public static SimAction Wait()
        {
            return new SimAction { Queen = QueenAction.Wait(), Train = NoTrain, BuildTypeValid = true };
        }

        public static SimAction Parse(string queenLine, string trainLine)
        {
            var a = new SimAction { Queen = QueenAction.Wait(), Train = NoTrain, BuildTypeValid = true };

            string[] t = (trainLine ?? "").Split(' ');
            if (t.Length == 0 || t[0] != "TRAIN") { a.Invalid = true; return a; }
            var ids = new List<int>();
            for (int i = 1; i < t.Length; i++)
            {
                int v;
                if (!int.TryParse(t[i], out v)) { a.Invalid = true; return a; }
                ids.Add(v);
            }
            a.Train = ids.ToArray();

            string[] q = (queenLine ?? "").Trim().Split(' ');
            switch (q[0])
            {
                case "WAIT":
                    if (q.Length != 1) a.Invalid = true;
                    break;
                case "MOVE":
                {
                    int x, y;
                    if (q.Length != 3 || !int.TryParse(q[1], out x) || !int.TryParse(q[2], out y)) { a.Invalid = true; break; }
                    a.Queen = new QueenAction { Kind = QueenActionKind.Move, X = x, Y = y };
                    break;
                }
                case "BUILD":
                {
                    int id;
                    if (q.Length != 3 || !int.TryParse(q[1], out id)) { a.Invalid = true; break; }
                    BuildType bt;
                    a.BuildTypeValid = TryParseBuild(q[2], out bt);
                    a.Queen = new QueenAction { Kind = QueenActionKind.Build, SiteId = id, Build = bt };
                    break;
                }
                default:
                    a.Invalid = true;
                    break;
            }
            return a;
        }

        public static bool TryParseBuild(string s, out BuildType t)
        {
            switch (s)
            {
                case "MINE": t = BuildType.Mine; return Rules.Mines;
                case "TOWER": t = BuildType.Tower; return Rules.Towers;
                case "BARRACKS-KNIGHT": t = BuildType.BarracksKnight; return true;
                case "BARRACKS-ARCHER": t = BuildType.BarracksArcher; return true;
                case "BARRACKS-GIANT": t = BuildType.BarracksGiant; return Rules.Giants;
                default: t = BuildType.Mine; return false;
            }
        }
    }
}
