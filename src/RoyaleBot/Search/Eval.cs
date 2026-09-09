using System;
using System.Runtime.CompilerServices;

namespace Royale
{
    /// <summary>Веса оценки позиции; все настраиваются через key=value (Program.Tuning).</summary>
    public sealed class EvalWeights
    {
        public double Hp = 100;             // моё HP
        public double EnemyHp = 50;         // HP противника
        public double Dead = 1e6;           // смерть королевы
        public double Tower = 0.25;         // HP моей башни (прокачка +96/ход не должна перевешивать поход к сайту)
        public double TowerBase = 400;      // сама башня (существует, с убыванием к концу)
        public double TowerNeeded = 800;    // первые TowerNeed башен при угрозе (у врага есть казарма рыцарей или рыцари)
        public double TowerNeededCalm = 500; // те же башни, пока угрозы нет
        public int TowerNeed = 3;
        public double Exposure = 4;         // за единицу расстояния королевы от безопасного места сверх SafeRadius при угрозе (тюнер 3b: 1 -> 4)
        public int SafeRadius = 250;
        public double ExposureEta = 0.5;    // штраф Exposure только за ту часть пути до укрытия, которую королева не успеет пройти до подхода ближайшего рыцаря (доля ExposureEta от его пути; 0 — за всё расстояние; self-play 0.8 против 0: 58:40, 0.5 против 0.8: 55:44)
        public double MineFar = 0.4;        // шахта на расстоянии MineFarDist от дома стоит на эту долю меньше
        public int MineFarDist = 1200;
        public double EnemyTower = 0.5;     // HP чужой башни
        public double EnemyTowerBase = 300;
        public double Mine = 4;             // за единицу будущей добычи моей шахты: min(доход × остаток ходов, золото сайта)
        public double EnemyMine = 2;        // то же для чужой шахты
        public double Gold = 4;             // золото в кармане (не больше GoldCap; без казармы ×0.2)
        public int GoldCap = 300;
        public double Knight = 5;           // HP моего рыцаря (× близость к чужой королеве)
        public double EnemyKnight = 20;     // HP чужого рыцаря
        public int KnightReach = 1200;      // дальше этого рыцарь стоит только FarKnight от полного веса (для своих рыцарей — близость к чужой королеве)
        public double FarKnight = 1.0;      // 1 = штраф не зависит от расстояния
        public double Giant = 2;            // HP моего гиганта
        public double EnemyGiant = 2;
        public double NoBarracks = 5000;    // нет ни одной казармы рыцарей: половина штрафа снимается по мере подхода к свободному сайту
        public double EconShort = 400;      // за единицу недобора дохода до EconTarget, половина снимается по мере подхода к свободному сайту с золотом на своей половине
        public int EconTarget = 6;
        public int ShapingDist = 1200;
        public double Readiness = 3;        // за каждую недостающую единицу HP своих башен рядом с королевой при угрозе (до DefenseNeed; тюнер 3b: 1.5 -> 3)
        public int DefenseNeed = 600;
        public int DefenseRadius = 450;
        public double GiantBarracks = 1500; // есть казарма гигантов, когда у врага >= GiantWhenTowers башен
        public int GiantWhenTowers = 2;
        public double ExtraBarracks = 500;  // каждая казарма сверх MaxBarracks
        public int MaxBarracks = 2;
        public double Cover = 300;          // королева под своей башней, когда есть угроза
        public double EnemyRange = 300;     // королева в радиусе чужой башни на листе: башня бьёт её каждый ход и дальше горизонта (шахты под чужой башней стоили 13 HP)
        public double Lead = 5000;          // лидерство по HP к концу партии (исход по лимиту ходов решает разница HP): Lead × tanh(разница / LeadScale), нарастает за LeadTurns ходов до конца
        public int LeadScale = 5;
        public int LeadTurns = 60;
        public double Noise = 1;            // случайный разброс для разнообразия равных вариантов

        public bool Set(string key, double v)
        {
            switch (key)
            {
                case "hp": Hp = v; break;
                case "ehp": EnemyHp = v; break;
                case "tower": Tower = v; break;
                case "towerbase": TowerBase = v; break;
                case "towerneed": TowerNeeded = v; break;
                case "towercalm": TowerNeededCalm = v; break;
                case "exposure": Exposure = v; break;
                case "safe": SafeRadius = (int)v; break;
                case "expeta": ExposureEta = v; break;
                case "minefar": MineFar = v; break;
                case "etowerbase": EnemyTowerBase = v; break;
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
                case "econshort": EconShort = v; break;
                case "econtarget": EconTarget = (int)v; break;
                case "lead": Lead = v; break;
                case "erange": EnemyRange = v; break;
                case "leadscale": LeadScale = (int)v; break;
                case "leadturns": LeadTurns = (int)v; break;
                case "readiness": Readiness = v; break;
                case "defneed": DefenseNeed = (int)v; break;
                case "defradius": DefenseRadius = (int)v; break;
                case "giantbar": GiantBarracks = v; break;
                case "goldcap": GoldCap = (int)v; break;
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
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static double Score(SimState s, int me, EvalWeights w, Random rnd)
        {
            int e = 1 - me;
            int myHp = s.Health[me], enHp = s.Health[e];
            if (myHp <= 0) return -w.Dead + s.Turn * 10;
            double v = w.Hp * myHp - w.EnemyHp * enHp;
            if (enHp <= 0) v += w.Dead * 0.1;
            // ценность башен убывает к концу партии; шахта оценивается будущей добычей до конца партии
            int left = Consts.MaxTurns - s.Turn;
            if (left < 1) left = 1;
            // исход по лимиту ходов: побеждает большее HP — к концу партии знак разницы важнее всего остального
            if (s.GameOver && s.Winner >= 0) v += s.Winner == me ? w.Dead * 0.1 : -w.Dead * 0.1;
            if (left < w.LeadTurns && w.Lead > 0)
                v += w.Lead * (1 - (double)left / w.LeadTurns) * Math.Tanh((myHp - enHp) / (double)w.LeadScale);
            double towerF = Math.Min(1.0, Math.Max(0.2, left / 40.0));

            int knightBarracks = 0, giantBarracks = 0, barracks = 0, enemyTowers = 0, myTowers = 0;
            bool enemyKnightsComing = false;
            double qx = s.QueenX[me], qy = s.QueenY[me];
            double homeX = me == 0 ? 200 : Consts.WorldWidth - 200, homeY = me == 0 ? 200 : Consts.WorldHeight - 200;
            bool covered = false;
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (st.Structure == StructureType.Barracks && st.Owner != me && st.CreepType == 0) enemyKnightsComing = true;
            }
            bool enemyKnightsAlive = false;
            double knightD2 = double.MaxValue;
            for (int i = 0; i < s.CreepCount[e]; i++)
                if (s.Creeps[e][i].Type == 0)
                {
                    enemyKnightsComing = true; enemyKnightsAlive = true;
                    double kd = SimState.D2(s.Creeps[e][i].X, s.Creeps[e][i].Y, qx, qy);
                    if (kd < knightD2) knightD2 = kd;
                }
            double safeD2 = SimState.D2(homeX, homeY, qx, qy);
            double enemyHomeX = Consts.WorldWidth - homeX, enemyHomeY = Consts.WorldHeight - homeY;
            double dFree = double.MaxValue, dGold = double.MaxValue;
            int income = 0, towerHpNear = 0;
            double defR2 = (double)w.DefenseRadius * w.DefenseRadius;
            for (int i = 0; i < s.Sites.Length; i++)
            {
                SimSite st = s.Sites[i];
                if (st.Structure == StructureType.None)
                {
                    double d = SimState.D2(st.X, st.Y, qx, qy);
                    if (d < dFree) dFree = d;
                    if (st.Gold != 0 && d < dGold && SimState.D2(st.X, st.Y, homeX, homeY) < SimState.D2(st.X, st.Y, enemyHomeX, enemyHomeY)) dGold = d;
                }
                switch (st.Structure)
                {
                    case StructureType.Tower:
                        if (st.Owner == me)
                        {
                            myTowers++;
                            double baseV = myTowers <= w.TowerNeed ? (enemyKnightsComing ? w.TowerNeeded : w.TowerNeededCalm) : w.TowerBase;
                            v += (baseV + w.Tower * st.Hp) * towerF;
                            double d2 = SimState.D2(st.X, st.Y, qx, qy);
                            if (!covered && d2 < (double)st.AttackRadius * st.AttackRadius) covered = true;
                            if (d2 < defR2) towerHpNear += st.Hp;
                            if (st.Hp >= 100 && d2 < safeD2) safeD2 = d2;
                        }
                        else
                        {
                            v -= (w.EnemyTowerBase + w.EnemyTower * st.Hp) * towerF;
                            enemyTowers++;
                            if (w.EnemyRange > 0 && SimState.D2(st.X, st.Y, qx, qy) < (double)st.AttackRadius * st.AttackRadius) v -= w.EnemyRange;
                        }
                        break;
                    case StructureType.Mine:
                    {
                        double yield = st.Rate * left;
                        if (st.Gold >= 0 && st.Gold < yield) yield = st.Gold;
                        if (st.Owner == me)
                        {
                            income += st.Rate;
                            double dHome = Math.Sqrt(SimState.D2(st.X, st.Y, homeX, homeY));
                            v += w.Mine * yield * (1 - w.MineFar * Math.Min(1.0, dHome / w.MineFarDist));
                        }
                        else v -= w.EnemyMine * yield;
                    }
                        break;
                    case StructureType.Barracks:
                        if (st.Owner == me)
                        {
                            barracks++;
                            if (st.CreepType == 0) knightBarracks++;
                            else if (st.CreepType == 2) giantBarracks++;
                        }
                        else if (st.CreepType == 0) enemyKnightsComing = true;
                        break;
                }
            }
            if (knightBarracks == 0)
                v -= w.NoBarracks * (dFree == double.MaxValue ? 1.0 : 0.5 + 0.5 * Math.Min(1.0, Math.Sqrt(dFree) / w.ShapingDist));
            int shortfall = w.EconTarget - income;
            if (shortfall > 0 && dGold != double.MaxValue)
                v -= w.EconShort * shortfall * (0.5 + 0.5 * Math.Min(1.0, Math.Sqrt(dGold) / w.ShapingDist));
            if (giantBarracks > 0 && knightBarracks > 0 && enemyTowers >= w.GiantWhenTowers && left >= 40) v += w.GiantBarracks;   // в конце партии гиганты не нужны (см. Macro.GiantMinLeft)
            if (barracks > w.MaxBarracks) v -= w.ExtraBarracks * (barracks - w.MaxBarracks);
            double gold = Math.Min(s.Gold[me], w.GoldCap);
            v += w.Gold * gold * (knightBarracks > 0 ? 1.0 : 0.2);

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
            if (enemyKnightsComing && towerHpNear < w.DefenseNeed) v -= w.Readiness * (w.DefenseNeed - towerHpNear);
            if (enemyKnightsAlive)
            {
                double safeD = Math.Sqrt(safeD2);
                double late = safeD - w.SafeRadius;
                // королева успевает в укрытие раньше рыцарей: её путь минус то, что она пройдёт, пока ближайший рыцарь идёт к ней
                if (w.ExposureEta > 0) late -= Math.Sqrt(knightD2) * w.ExposureEta * Consts.QueenSpeed / CreepStats.Speed[0];
                if (late > 0) v -= w.Exposure * late * (1 + Math.Max(0, 80 - myHp) / 40.0);
            }

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
