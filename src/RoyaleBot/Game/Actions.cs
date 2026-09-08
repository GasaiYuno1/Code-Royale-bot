using System.Text;

namespace Royale
{
    public enum BuildType { Mine, Tower, BarracksKnight, BarracksArcher, BarracksGiant }

    public enum QueenActionKind { Wait, Move, Build }

    /// <summary>Первая строка ответа: WAIT | MOVE x y | BUILD id TYPE.</summary>
    public struct QueenAction
    {
        public QueenActionKind Kind;
        public int X, Y;
        public int SiteId;
        public BuildType Build;

        public static QueenAction Wait()
        {
            return new QueenAction { Kind = QueenActionKind.Wait };
        }

        public static QueenAction Move(int x, int y)
        {
            return new QueenAction
            {
                Kind = QueenActionKind.Move,
                X = Geom.Clamp(x, 0, Consts.WorldWidth),
                Y = Geom.Clamp(y, 0, Consts.WorldHeight),
            };
        }

        /// <summary>BUILD на сайт, которого королева не касается, для рефери равен MOVE к его центру.</summary>
        public static QueenAction BuildAt(int siteId, BuildType type)
        {
            return new QueenAction { Kind = QueenActionKind.Build, SiteId = siteId, Build = type };
        }

        public static string BuildName(BuildType t)
        {
            switch (t)
            {
                case BuildType.Mine: return "MINE";
                case BuildType.Tower: return "TOWER";
                case BuildType.BarracksKnight: return "BARRACKS-KNIGHT";
                case BuildType.BarracksArcher: return "BARRACKS-ARCHER";
                default: return "BARRACKS-GIANT";
            }
        }

        /// <summary>Разрешена ли постройка в текущей лиге (иначе рефери убивает бота).</summary>
        public static bool Allowed(BuildType t)
        {
            switch (t)
            {
                case BuildType.Mine: return Rules.Mines;
                case BuildType.Tower: return Rules.Towers;
                case BuildType.BarracksGiant: return Rules.Giants;
                default: return true;
            }
        }

        public string Format()
        {
            switch (Kind)
            {
                case QueenActionKind.Move: return "MOVE " + X + " " + Y;
                case QueenActionKind.Build: return "BUILD " + SiteId + " " + BuildName(Build);
                default: return "WAIT";
            }
        }

        public override string ToString() { return Format(); }
    }

    /// <summary>Ответ за ход: действие королевы и список казарм для TRAIN.</summary>
    public struct TurnOutput
    {
        public QueenAction Queen;
        public int[] Train;

        public static TurnOutput Idle
        {
            get { return new TurnOutput { Queen = QueenAction.Wait(), Train = new int[0] }; }
        }

        public string TrainLine()
        {
            if (Train == null || Train.Length == 0) return "TRAIN";
            var sb = new StringBuilder("TRAIN");
            foreach (int id in Train) sb.Append(' ').Append(id);
            return sb.ToString();
        }
    }
}
