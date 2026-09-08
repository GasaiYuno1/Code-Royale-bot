using System;

namespace Royale
{
    /// <summary>Веса оценки позиции; все настраиваются через key=value (Program.Tuning).</summary>
    public sealed class EvalWeights
    {
        public double Hp = 100;             // моё HP
        public double EnemyHp = 30;         // HP противника
        public double Dead = 1e6;           // смерть королевы
        public double Tower = 3.0;          // HP моей башни
        public double EnemyTower = 1.0;     // HP чужой башни
        public double Mine = 300;           // единица дохода моей шахты
        public double EnemyMine = 100;      // единица дохода чужой шахты
        public double Gold = 3;             // золото в кармане
        public double Knight = 15;           // HP моего рыцаря (× близость к чужой королеве)
        public double EnemyKnight = 60;     // HP чужого рыцаря (× близость к моей королеве)
        public int KnightReach = 1200;      // дальше этого рыцарь стоит только FarKnight от полного веса (для своих рыцарей — близость к чужой королеве)
        public double FarKnight = 1.0;      // 1 = штраф не зависит от расстояния
        public double Giant = 2;            // HP моего гиганта
        public double EnemyGiant = 2;
        public double NoBarracks = 2000;    // нет ни одной казармы рыцарей
        public double ExtraBarracks = 500;  // каждая казарма сверх MaxBarracks
        public int MaxBarracks = 2;
        public double Cover = 300;          // королева под своей башней, когда есть угроза
        public double Noise = 1;            // случайный разброс для разнообразия равных вариантов

        public bool Set(string key, double v)
        {
            switch (key)
            {
                case "hp": Hp = v; break;
                case "ehp": EnemyHp = v; break;
                case "tower": Tower = v; break;
                case "etower": EnemyTower = v; break;
                case "mine": Mine = v; break;
                case "emine": EnemyMine = v; break;
                case "gold": Gold = v; break;
                case "knight": Knight = v; break;
                case "eknight": EnemyKnight = v; break;
                case "reach": KnightReach = (int)v; break;
                case "far": FarKnight = v; break;
                case "giant": Giant = v; break;
                case "egiant": EnemyGiant = v; break;
                case "nobar": NoBarracks = v; break;
                case "extrabar": ExtraBarracks = v; break;
                case "maxbar": MaxBarracks = (int)v; break;
                case "cover": Cover = v; break;
                case "noise": Noise = v; break;
                default: return false;
            }
            return true;
        }
    }

    public static class Eval
    {
        /// <summary>Оценка состояния глазами игрока me (больше — лучше).</summary>
        public static double Score(SimState s, int me, EvalWeights w, Random rnd)
        {
            int e = 1 - me;
            int myHp = s.Health[me], enHp = s.Health[e];
            if (myHp <= 0) return -w.Dead + s.Turn * 10;
            double v = w.Hp * myHp - w.EnemyHp * enHp;
            if (enHp <= 0) v += w.Dead * 0.1;
            // ценность экономики и башен убывает к концу партии: шахта на 190-м ходу почти ничего не даст
            int left = Consts.MaxTurns - s.Turn;
            double mineF = Math.Min(1.0, Math.Max(0.15, left / 80.0));
            double towerF = Math.Min(1.0, Math.Max(0.2, left / 40.0));

            int knightBarracks = 0, barracks = 0;
            bool enemyKnightsComing = false;
            double qx = s.QueenX[me], qy = s.QueenY[me];
            bool covered = false;
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                switch (st.Structure)
                {
                    case StructureType.Tower:
                        if (st.Owner == me)
                        {
                            v += w.Tower * st.Hp * towerF;
                            if (!covered && SimState.D2(st.X, st.Y, qx, qy) < (double)st.AttackRadius * st.AttackRadius) covered = true;
                        }
                        else v -= w.EnemyTower * st.Hp * towerF;
                        break;
                    case StructureType.Mine:
                        if (st.Owner == me) v += w.Mine * st.Rate * mineF;
                        else v -= w.EnemyMine * st.Rate * mineF;
                        break;
                    case StructureType.Barracks:
                        if (st.Owner == me)
                        {
                            barracks++;
                            if (st.CreepType == 0) knightBarracks++;
                        }
                        else if (st.CreepType == 0 && st.Training) enemyKnightsComing = true;
                        break;
                }
            }
            if (knightBarracks == 0) v -= w.NoBarracks;
            if (barracks > w.MaxBarracks) v -= w.ExtraBarracks * (barracks - w.MaxBarracks);
            v += w.Gold * s.Gold[me];

            double eqx = s.QueenX[e], eqy = s.QueenY[e];
            for (int i = 0; i < s.CreepCount[me]; i++)
            {
                SimUnit c = s.Creeps[me][i];
                if (c.Type == 0) v += w.Knight * c.Health * Closeness(c.X, c.Y, eqx, eqy, w);
                else if (c.Type == 2) v += w.Giant * c.Health;
            }
            for (int i = 0; i < s.CreepCount[e]; i++)
            {
                SimUnit c = s.Creeps[e][i];
                if (c.Type == 0)
                {
                    v -= w.EnemyKnight * c.Health;
                    enemyKnightsComing = true;
                }
                else if (c.Type == 2) v -= w.EnemyGiant * c.Health;
            }
            if (enemyKnightsComing && covered) v += w.Cover;

            if (w.Noise > 0) v += (rnd.NextDouble() - 0.5) * w.Noise;
            return v;
        }

        /// <summary>1 вплотную к цели, линейно до FarKnight на расстоянии KnightReach и дальше.</summary>
        private static double Closeness(double x, double y, double tx, double ty, EvalWeights w)
        {
            double d = Math.Sqrt(SimState.D2(x, y, tx, ty));
            if (d >= w.KnightReach) return w.FarKnight;
            return w.FarKnight + (1 - w.FarKnight) * (1 - d / w.KnightReach);
        }
    }
}
