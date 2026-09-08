using System;
using System.Collections.Generic;
using System.Text;

namespace Royale
{
    /// <summary>Сайт с постройкой, как Obstacle + Structure в рефери.</summary>
    public struct SimSite
    {
        public int Id, X, Y, Radius;
        public double Area;             // Math.PI * radius * radius, как в Obstacle.area
        public int Gold;                // -1 неизвестно
        public int MaxMineSize;         // -1 неизвестно
        public StructureType Structure;
        public int Owner;               // абсолютный индекс игрока (0 первый, 1 второй)
        public int Rate;                // шахта: доход
        public int Hp, AttackRadius;    // башня
        public int CreepType, Progress; // казарма
        public bool Training;

        public void Clear()
        {
            Structure = StructureType.None; Owner = -1; Rate = 0; Hp = 0; AttackRadius = 0; CreepType = 0; Progress = 0; Training = false;
        }
    }

    /// <summary>Крип. Координаты double внутри хода, целые между ходами.</summary>
    public struct SimUnit
    {
        public double X, Y;
        public int Type;       // 0 рыцарь, 1 лучник, 2 гигант
        public int Health;
        public int Radius, Mass;
    }

    /// <summary>
    /// Состояние партии для симулятора. Игроки по абсолютному индексу: 0 — первый (старт в левом верхнем углу).
    /// Порядок сущностей как в рефери: крипы игрока 0, крипы игрока 1, королева 0, королева 1, сайты по id.
    /// </summary>
    public sealed partial class SimState
    {
        public int Turn;               // ход рефери, с 0; gameTurn(turn) — 200 ходов
        public int MyIndex;            // чей это ввод (информационно)
        public SimSite[] Sites;
        public SimUnit[][] Creeps = new SimUnit[2][];
        public int[] CreepCount = new int[2];
        public double[] QueenX = new double[2], QueenY = new double[2];
        public int[] Health = new int[2];
        public int[] Gold = new int[2];   // Gold[1 - MyIndex] может быть неизвестен (-1)
        public bool GameOver;
        public int Winner = -1;           // -1 ничья или не окончена
        public bool[] Killed = new bool[2];

        public SimState()
        {
            Creeps[0] = new SimUnit[32];
            Creeps[1] = new SimUnit[32];
        }

        public int OtherIndex { get { return 1 - MyIndex; } }

        /// <summary>
        /// Состояние из ввода одного игрока (mine) и, если есть, второго (other) за тот же ход.
        /// Ввод второго даёт его золото и видимость его сайтов. Абсолютный индекс: первая королева в списке юнитов —
        /// королева игрока 0.
        /// </summary>
        public static SimState FromInputs(TurnInput mine, TurnInput other, int turn)
        {
            return FromInputs(mine, other, turn, null);
        }

        /// <param name="carry">Состояние после предыдущего хода симулятора: его золото сайтов используется там,
        /// где ввод ничего не показывает (сверка по логам).</param>
        public static SimState FromInputs(TurnInput mine, TurnInput other, int turn, SimState carry)
        {
            var s = new SimState();
            s.Turn = turn;
            int my = -1;
            foreach (UnitInfo u in mine.Units)
            {
                if (u.IsQueen) { my = u.Owner == 0 ? 0 : 1; break; }
            }
            if (my < 0) throw new InvalidOperationException("no queen in input");
            s.MyIndex = my;

            s.Sites = new SimSite[mine.Sites.Length];
            for (int i = 0; i < mine.Sites.Length; i++)
            {
                Site a = mine.Sites[i];
                Site b = other != null ? other.Sites[i] : a;
                var site = new SimSite();
                site.Id = a.Id; site.X = a.X; site.Y = a.Y; site.Radius = a.Radius;
                site.Area = Math.PI * a.Radius * a.Radius;
                site.Gold = MergeGold(a.Gold, b.Gold, a.KnownGold, b.KnownGold);
                if (a.Gold < 0 && b.Gold < 0 && carry != null && carry.Sites.Length == mine.Sites.Length && carry.Sites[i].Gold >= 0)
                    site.Gold = carry.Sites[i].Gold;
                site.MaxMineSize = Math.Max(a.MaxMineSize >= 0 ? a.MaxMineSize : a.KnownMaxMineSize, b.MaxMineSize >= 0 ? b.MaxMineSize : b.KnownMaxMineSize);
                site.Structure = a.Structure;
                site.Owner = a.Structure == StructureType.None ? -1 : (a.Owner == 0 ? my : 1 - my);
                switch (a.Structure)
                {
                    case StructureType.Mine:
                        site.Rate = Math.Max(a.Param1, b.Param1);
                        break;
                    case StructureType.Tower:
                        site.Hp = a.Param1; site.AttackRadius = a.Param2;
                        break;
                    case StructureType.Barracks:
                        site.CreepType = a.Param2;
                        site.Training = a.Param1 > 0;
                        site.Progress = a.Param1 > 0 ? CreepStats.BuildTime[a.Param2] - a.Param1 : 0;
                        break;
                }
                s.Sites[i] = site;
            }

            s.Gold[my] = mine.Gold;
            s.Gold[1 - my] = other != null ? other.Gold : -1;
            foreach (UnitInfo u in mine.Units)
            {
                int p = u.Owner == 0 ? my : 1 - my;
                if (u.IsQueen)
                {
                    s.QueenX[p] = u.X; s.QueenY[p] = u.Y; s.Health[p] = u.Health;
                }
                else
                {
                    int type = (int)u.Type;
                    var c = new SimUnit { X = u.X, Y = u.Y, Type = type, Health = u.Health, Radius = CreepStats.Radius[type], Mass = CreepStats.Mass[type] };
                    s.AddCreep(p, c);
                }
            }
            if (turn == 0) s.RestoreInitialQueens();
            return s;
        }

        /// <summary>
        /// Первый ход: во вводе позиции королев округлены, а внутри рефери они дробные — результат расталкивания
        /// от стартовых углов (200,200) и (1720,800) с сайтами (Referee.init). Повторяем его, чтобы стартовать бит-в-бит.
        /// </summary>
        public void RestoreInitialQueens()
        {
            if (CreepCount[0] != 0 || CreepCount[1] != 0) return;
            double x0 = QueenX[0], y0 = QueenY[0], x1 = QueenX[1], y1 = QueenY[1];
            QueenX[0] = 200; QueenY[0] = 200;
            QueenX[1] = Consts.WorldWidth - 200; QueenY[1] = Consts.WorldHeight - 200;
            FixCollisions(999);
            if (JavaMath.Round(QueenX[0]) != (int)x0 || JavaMath.Round(QueenY[0]) != (int)y0 ||
                JavaMath.Round(QueenX[1]) != (int)x1 || JavaMath.Round(QueenY[1]) != (int)y1)
            {
                QueenX[0] = x0; QueenY[0] = y0; QueenX[1] = x1; QueenY[1] = y1;
            }
        }

        /// <summary>Золото сайта: видимое сейчас значение важнее памяти; из двух воспоминаний — меньшее (золото только убывает).</summary>
        private static int MergeGold(int nowA, int nowB, int memA, int memB)
        {
            if (nowA >= 0) return nowA;
            if (nowB >= 0) return nowB;
            if (memA >= 0 && memB >= 0) return Math.Min(memA, memB);
            return memA >= 0 ? memA : memB;
        }

        public void AddCreep(int p, SimUnit c)
        {
            if (CreepCount[p] == Creeps[p].Length) Array.Resize(ref Creeps[p], Creeps[p].Length * 2);
            Creeps[p][CreepCount[p]++] = c;
        }

        public SimState Clone()
        {
            var s = new SimState();
            s.CopyFrom(this);
            return s;
        }

        public void CopyFrom(SimState o)
        {
            Turn = o.Turn; MyIndex = o.MyIndex; GameOver = o.GameOver; Winner = o.Winner;
            if (Sites == null || Sites.Length != o.Sites.Length) Sites = new SimSite[o.Sites.Length];
            Array.Copy(o.Sites, Sites, o.Sites.Length);
            for (int p = 0; p < 2; p++)
            {
                if (Creeps[p].Length < o.CreepCount[p]) Creeps[p] = new SimUnit[o.Creeps[p].Length];
                Array.Copy(o.Creeps[p], Creeps[p], o.CreepCount[p]);
                CreepCount[p] = o.CreepCount[p];
                QueenX[p] = o.QueenX[p]; QueenY[p] = o.QueenY[p];
                Health[p] = o.Health[p]; Gold[p] = o.Gold[p]; Killed[p] = o.Killed[p];
            }
        }

        /// <summary>Ввод хода глазами игрока p, как Referee.sendGameStates; неизвестные симулятору значения — "?".</summary>
        public void ToInputLines(int p, List<string> lines)
        {
            double qx = QueenX[p], qy = QueenY[p];
            int touched = -1, touchedCount = 0;
            for (int i = 0; i < Sites.Length; i++)
            {
                double d2 = D2(Sites[i].X, Sites[i].Y, qx, qy);
                int lim = Sites[i].Radius + Consts.QueenRadius + Consts.TouchingDelta;
                if (d2 < (double)(lim * lim)) { touched = Sites[i].Id; touchedCount++; }
            }
            if (touchedCount != 1) touched = -1;
            lines.Add(Gold[p] + " " + touched);

            var sb = new StringBuilder();
            for (int i = 0; i < Sites.Length; i++)
            {
                SimSite s = Sites[i];
                bool visible = (s.Structure != StructureType.None && s.Owner == p) || D2(s.X, s.Y, qx, qy) < (double)(Consts.QueenVision * Consts.QueenVision);
                sb.Clear();
                sb.Append(s.Id).Append(' ');
                sb.Append(visible ? Unknown(s.Gold) : "-1").Append(' ');
                sb.Append(visible ? Unknown(s.MaxMineSize) : "-1").Append(' ');
                int ownerRel = s.Owner == p ? 0 : 1;
                switch (s.Structure)
                {
                    case StructureType.Mine:
                        sb.Append("0 ").Append(ownerRel).Append(' ').Append(visible ? s.Rate.ToString() : "-1").Append(" -1");
                        break;
                    case StructureType.Tower:
                        sb.Append("1 ").Append(ownerRel).Append(' ').Append(s.Hp).Append(' ').Append(s.AttackRadius);
                        break;
                    case StructureType.Barracks:
                        sb.Append("2 ").Append(ownerRel).Append(' ').Append(s.Training ? CreepStats.BuildTime[s.CreepType] - s.Progress : 0).Append(' ').Append(s.CreepType);
                        break;
                    default:
                        sb.Append("-1 -1 -1 -1");
                        break;
                }
                lines.Add(sb.ToString());
            }

            lines.Add((CreepCount[0] + CreepCount[1] + 2).ToString());
            for (int owner = 0; owner < 2; owner++)
            {
                int rel = owner == p ? 0 : 1;
                for (int i = 0; i < CreepCount[owner]; i++)
                {
                    SimUnit c = Creeps[owner][i];
                    lines.Add(JavaMath.Round(c.X) + " " + JavaMath.Round(c.Y) + " " + rel + " " + c.Type + " " + c.Health);
                }
                lines.Add(JavaMath.Round(QueenX[owner]) + " " + JavaMath.Round(QueenY[owner]) + " " + rel + " -1 " + Health[owner]);
            }
        }

        private static string Unknown(int v) { return v < 0 ? "?" : v.ToString(); }

        public static double D2(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return dx * dx + dy * dy;
        }
    }
}
