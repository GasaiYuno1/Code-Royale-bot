using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Royale
{
    /// <summary>
    /// Ход рефери (Referee.gameTurn) построчно: действия игроков → крипы (5 подшагов с расталкиванием) →
    /// постройки → фиксированный доход (Wood) → удаление мёртвых → проверка королев → округление координат.
    /// Порядок операций и арифметика в double повторяют Kotlin-код, чтобы состояние сходилось бит-в-бит.
    /// </summary>
    public sealed partial class SimState
    {
        private struct Body { public double X, Y; public int Radius, Mass; }
        private struct CreepRef { public int P, I; }

        private Body[] _bodies = new Body[128];
        private int _nBodies;
        private CreepRef[] _order = new CreepRef[64];
        private int _nOrder;
        private readonly int[] _attempted = new int[2];
        private readonly int[] _schedP = new int[2], _schedSite = new int[2];
        private readonly BuildType[] _schedType = new BuildType[2];
        private readonly bool[] _schedValid = new bool[2];

        private const double Frame = 1.0 / 5;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Step(SimAction a0, SimAction a1)
        {
            if (TowerTargetMode >= 5)
                for (int p = 0; p < 2; p++) for (int i = 0; i < CreepCount[p]; i++) Creeps[p][i].ShotThisTurn = false;
            if (GameOver) return;
            PrevQueenX[0] = QueenX[0]; PrevQueenY[0] = QueenY[0]; PrevQueenX[1] = QueenX[1]; PrevQueenY[1] = QueenY[1];
            long t0 = Profile ? Stopwatch.GetTimestamp() : 0;
            if (CreepsFirst) ProcessCreeps();
            ProcessPlayerActions(a0, a1);
            if (GameOver) { Turn++; return; }
            long t1 = Profile ? Stopwatch.GetTimestamp() : 0;
            if (!CreepsFirst) ProcessCreeps();
            long t2 = Profile ? Stopwatch.GetTimestamp() : 0;
            for (int i = 0; i < Sites.Length; i++) ActSite(i);
            if (Rules.FixedIncome)
            {
                Gold[0] += Consts.WoodFixedIncome;
                Gold[1] += Consts.WoodFixedIncome;
            }
            long t3 = Profile ? Stopwatch.GetTimestamp() : 0;
            RemoveDead();
            CheckEnd();
            Snap();
            Turn++;
            if (Profile)
            {
                long t4 = Stopwatch.GetTimestamp();
                ProfActions += t1 - t0; ProfCreeps += t2 - t1; ProfSites += t3 - t2; ProfEnd += t4 - t3;
            }
        }

        /// <summary>Профиль фаз шага (bench profile=1): тики Stopwatch по фазам; внутри ProcessCreeps отдельно движение/расталкивание/урон.</summary>
        public static bool Profile;
        public static long ProfActions, ProfCreeps, ProfSites, ProfEnd, ProfMove, ProfCollide, ProfDamage, ProfTail, ProfLoad, ProfBuild, ProfPass, ProfStore;

        // ---------- действия игроков ----------

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProcessPlayerActions(SimAction a0, SimAction a1)
        {
            int nAttempted = 0, nSched = 0;
            for (int p = 0; p < 2; p++)
            {
                SimAction a = p == 0 ? a0 : a1;
                if (a.Invalid) { Kill(p); continue; }

                // TRAIN: любое предупреждение отменяет всю команду целиком
                bool ok = true;
                int sum = 0;
                for (int i = 0; i < a.Train.Length && ok; i++)
                {
                    int idx = SiteIndex(a.Train[i]);
                    if (idx < 0) { Kill(p); ok = false; break; }
                    SimSite s = Sites[idx];
                    if (s.Structure != StructureType.Barracks || s.Owner != p || s.Training) ok = false;
                    else sum += CreepStats.Cost[s.CreepType];
                    for (int j = 0; j < i; j++) if (a.Train[j] == a.Train[i]) ok = false;
                }
                if (Killed[p]) continue;
                if (ok && sum > Gold[p]) ok = false;
                if (ok)
                {
                    Gold[p] -= sum;
                    for (int i = 0; i < a.Train.Length; i++)
                    {
                        int idx = SiteIndex(a.Train[i]);
                        Sites[idx].Progress = 0;
                        Sites[idx].Training = true;
                    }
                }

                // команда королевы
                switch (a.Queen.Kind)
                {
                    case QueenActionKind.Move:
                        Towards(ref QueenX[p], ref QueenY[p], a.Queen.X, a.Queen.Y, (double)Consts.QueenSpeed);
                        break;
                    case QueenActionKind.Build:
                    {
                        int idx = SiteIndex(a.Queen.SiteId);
                        if (idx < 0) { Kill(p); break; }
                        SimSite s = Sites[idx];
                        int lim = Consts.QueenRadius + s.Radius + Consts.TouchingDelta;
                        if (D2(s.X, s.Y, QueenX[p], QueenY[p]) < (double)(lim * lim))
                        {
                            if (s.Structure != StructureType.None && s.Owner == 1 - p) break;                       // warning: owned by enemy
                            if (s.Structure == StructureType.Barracks && s.Owner == p && s.Training) break;        // warning: training
                            _attempted[nAttempted++] = idx;
                            _schedP[nSched] = p; _schedSite[nSched] = idx; _schedType[nSched] = a.Queen.Build; _schedValid[nSched] = a.BuildTypeValid;
                            nSched++;
                        }
                        else
                        {
                            Towards(ref QueenX[p], ref QueenY[p], s.X, s.Y, (double)Consts.QueenSpeed);
                        }
                        break;
                    }
                }
            }

            if (nAttempted == 2 && _attempted[0] == _attempted[1])
            {
                int drop = Turn % 2;
                if (drop == 0) { _schedP[0] = _schedP[1]; _schedSite[0] = _schedSite[1]; _schedType[0] = _schedType[1]; _schedValid[0] = _schedValid[1]; }
                nSched = 1;
            }

            for (int k = 0; k < nSched; k++)
            {
                int p = _schedP[k], idx = _schedSite[k];
                if (!_schedValid[k]) { Kill(p); continue; }
                SimSite s = Sites[idx];
                switch (_schedType[k])
                {
                    case BuildType.Mine:
                        if (s.Structure == StructureType.Mine)
                        {
                            s.Rate++;
                            if (s.MaxMineSize >= 0 && s.Rate > s.MaxMineSize) s.Rate = s.MaxMineSize;
                        }
                        else { s.Clear(); s.Structure = StructureType.Mine; s.Owner = p; s.Rate = 1; }
                        break;
                    case BuildType.Tower:
                        if (s.Structure == StructureType.Tower)
                        {
                            s.Hp += Consts.TowerHpIncrement;
                            if (s.Hp > Consts.TowerHpMax) s.Hp = Consts.TowerHpMax;
                        }
                        else { s.Clear(); s.Structure = StructureType.Tower; s.Owner = p; s.Hp = Consts.TowerHpInitial; s.AttackRadius = 0; }
                        break;
                    default:
                        s.Clear(); s.Structure = StructureType.Barracks; s.Owner = p;
                        s.CreepType = _schedType[k] == BuildType.BarracksKnight ? 0 : _schedType[k] == BuildType.BarracksArcher ? 1 : 2;
                        break;
                }
                Sites[idx] = s;
            }
        }

        private void Kill(int p)
        {
            Killed[p] = true;
            GameOver = true;
            Winner = Killed[1 - p] ? -1 : 1 - p;
        }

        public int SiteIndex(int id)
        {
            if (id >= 0 && id < Sites.Length && Sites[id].Id == id) return id;
            for (int i = 0; i < Sites.Length; i++) if (Sites[i].Id == id) return i;
            return -1;
        }

        // ---------- крипы ----------

        private void BuildOrder()
        {
            int n = CreepCount[0] + CreepCount[1];
            if (_order.Length < n) _order = new CreepRef[Math.Max(n, _order.Length * 2)];
            _nOrder = 0;
            for (int type = 0; type < 3; type++)
                for (int p = 0; p < 2; p++)
                    for (int i = 0; i < CreepCount[p]; i++)
                        if (Creeps[p][i].Type == type) _order[_nOrder++] = new CreepRef { P = p, I = i };
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ProcessCreeps()
        {
            BuildOrder();
            for (int sub = 0; sub < 5; sub++)
            {
                long m0 = Profile ? Stopwatch.GetTimestamp() : 0;
                for (int k = 0; k < _nOrder; k++) MoveCreep(_order[k].P, _order[k].I);
                long m1 = Profile ? Stopwatch.GetTimestamp() : 0;
                FixCollisions(SubstepIterations, sub == 0);
                if (Profile) { long m2 = Stopwatch.GetTimestamp(); ProfMove += m1 - m0; ProfCollide += m2 - m1; }
            }
            long d0 = Profile ? Stopwatch.GetTimestamp() : 0;
            for (int k = 0; k < _nOrder; k++) DealDamage(_order[k].P, _order[k].I);
            if (Profile) { long d1 = Stopwatch.GetTimestamp(); ProfDamage += d1 - d0; _tailStart = d1; }

            // чужие шахты сносятся любым крипом, коснувшимся своего ближайшего сайта
            for (int k = 0; k < _nOrder; k++)
            {
                SimUnit c = Creeps[_order[k].P][_order[k].I];
                int idx = SubstepIterations > 0 ? ClosestSiteNear(BodyIndexOf(_order[k].P, _order[k].I), c.X, c.Y) : ClosestSite(c.X, c.Y);
                if (idx < 0) continue;
                int lim = Sites[idx].Radius + c.Radius + Consts.TouchingDelta;
                if (D2(Sites[idx].X, Sites[idx].Y, c.X, c.Y) >= (double)(lim * lim)) continue;
                if (Sites[idx].Structure == StructureType.Mine && Sites[idx].Owner != _order[k].P) Sites[idx].Clear();
            }

            for (int k = 0; k < _nOrder; k++)
            {
                ref SimUnit c = ref Creeps[_order[k].P][_order[k].I];
                c.Health -= 1;
                if (c.Health < 0) c.Health = 0;
            }

            // королевы сносят чужие шахты и казармы (не башни)
            for (int p = 0; p < 2; p++)
            {
                int idx = ClosestSite(QueenX[p], QueenY[p]);
                int lim = Sites[idx].Radius + Consts.QueenRadius + Consts.TouchingDelta;
                if (D2(Sites[idx].X, Sites[idx].Y, QueenX[p], QueenY[p]) >= (double)(lim * lim)) continue;
                StructureType st = Sites[idx].Structure;
                if ((st == StructureType.Mine || st == StructureType.Barracks) && Sites[idx].Owner != p) Sites[idx].Clear();
            }
            if (Profile) ProfTail += Stopwatch.GetTimestamp() - _tailStart;
        }
        private long _tailStart;

        private int ClosestSite(double x, double y)
        {
            int best = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i < Sites.Length; i++)
            {
                double d = D2(Sites[i].X, Sites[i].Y, x, y);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Ближайший крип противника игрока p (включая умерших в этот ход, но ещё не удалённых); -1 если нет.</summary>
        private int ClosestEnemyCreep(int p, double x, double y)
        {
            int e = 1 - p, best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < CreepCount[e]; i++)
            {
                double d = D2(Creeps[e][i].X, Creeps[e][i].Y, x, y);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Цель башни по TowerTargetMode среди чужих крипов в радиусе; -1 если нет.</summary>
        private int TowerTarget(SimSite s, int e, double r2)
        {
            int best = -1;
            int bestHp = int.MaxValue;
            double bestD = double.MaxValue;
            double qx = QueenX[s.Owner], qy = QueenY[s.Owner];
            for (int i = 0; i < CreepCount[e]; i++)
            {
                if (D2(Creeps[e][i].X, Creeps[e][i].Y, s.X, s.Y) >= r2) continue;
                switch (TowerTargetMode)
                {
                    case 1: if (Creeps[e][i].Health < bestHp) { bestHp = Creeps[e][i].Health; best = i; } break;
                    case 2: if (best < 0) best = i; break;
                    case 4:
                    case 5:
                    {
                        if (TowerTargetMode == 5 && Creeps[e][i].ShotThisTurn) break;
                        double d = D2(Creeps[e][i].X, Creeps[e][i].Y, qx, qy);
                        if (d < bestD) { bestD = d; best = i; }
                        break;
                    }
                    case 6:
                    {
                        if (Creeps[e][i].ShotThisTurn) break;
                        double d = D2(Creeps[e][i].X, Creeps[e][i].Y, s.X, s.Y);
                        if (d < bestD) { bestD = d; best = i; }
                        break;
                    }
                    default: best = i; break;
                }
            }
            if (best >= 0) Creeps[e][best].ShotThisTurn = true;
            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void MoveCreep(int p, int i)
        {
            ref SimUnit c = ref Creeps[p][i];
            int e = 1 - p;
            switch (c.Type)
            {
                case 0: // рыцарь: к чужой королеве, пока не в контакте
                {
                    double qx = TargetPrevQueen ? PrevQueenX[e] : QueenX[e], qy = TargetPrevQueen ? PrevQueenY[e] : QueenY[e];
                    int lim = c.Radius + Consts.QueenRadius + CreepStats.Range[0];
                    if (D2(c.X, c.Y, qx, qy) > (double)(lim * lim))
                    {
                        double rx, ry;
                        Resized(c.X - qx, c.Y - qy, 3.0, out rx, out ry);
                        Towards(ref c.X, ref c.Y, qx + rx, qy + ry, (double)CreepStats.Speed[0] * Frame);
                    }
                    break;
                }
                case 1: // лучник: к ближайшему чужому крипу, иначе к своей королеве
                {
                    int t = ClosestEnemyCreep(p, c.X, c.Y);
                    double tx, ty; int tr;
                    if (t >= 0) { tx = Creeps[e][t].X; ty = Creeps[e][t].Y; tr = Creeps[e][t].Radius; }
                    else { tx = QueenX[p]; ty = QueenY[p]; tr = Consts.QueenRadius; }
                    int lim = c.Radius + tr + CreepStats.Range[1];
                    if (D2(c.X, c.Y, tx, ty) > (double)(lim * lim))
                    {
                        double rx, ry;
                        Resized(c.X - tx, c.Y - ty, 3.0, out rx, out ry);
                        Towards(ref c.X, ref c.Y, tx + rx, ty + ry, (double)CreepStats.Speed[1] * Frame);
                    }
                    break;
                }
                default: // гигант: к ближайшей чужой башне, без башен стоит
                {
                    int best = -1;
                    double bestD = double.MaxValue;
                    for (int s = 0; s < Sites.Length; s++)
                    {
                        if (Sites[s].Structure != StructureType.Tower || Sites[s].Owner != e) continue;
                        double d = D2(Sites[s].X, Sites[s].Y, c.X, c.Y);
                        if (d < bestD) { bestD = d; best = s; }
                    }
                    if (best >= 0) Towards(ref c.X, ref c.Y, Sites[best].X, Sites[best].Y, (double)CreepStats.Speed[2] * Frame);
                    break;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void DealDamage(int p, int i)
        {
            SimUnit c = Creeps[p][i];
            int e = 1 - p;
            switch (c.Type)
            {
                case 0:
                {
                    int lim = c.Radius + Consts.QueenRadius + CreepStats.Range[0] + Consts.TouchingDelta;
                    if (D2(c.X, c.Y, QueenX[e], QueenY[e]) < (double)(lim * lim))
                    {
                        Health[e] -= Consts.KnightDamage;
                        if (Health[e] < 0) Health[e] = 0;
                    }
                    break;
                }
                case 1:
                {
                    int t = ClosestEnemyCreep(p, c.X, c.Y);
                    if (t < 0) break;
                    ref SimUnit target = ref Creeps[e][t];
                    int lim = c.Radius + target.Radius + CreepStats.Range[1] + Consts.TouchingDelta;
                    if (D2(c.X, c.Y, target.X, target.Y) < (double)(lim * lim))
                        DamageCreep(ref target, target.Type == 2 ? Consts.ArcherDamageToGiants : Consts.ArcherDamage);
                    break;
                }
                default:
                {
                    for (int s = 0; s < Sites.Length; s++)
                    {
                        if (Sites[s].Structure != StructureType.Tower || Sites[s].Owner != e) continue;
                        int lim = c.Radius + Sites[s].Radius + Consts.TouchingDelta;
                        if (D2(Sites[s].X, Sites[s].Y, c.X, c.Y) < (double)(lim * lim))
                        {
                            Sites[s].Hp -= Consts.GiantBustRate;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        private static void DamageCreep(ref SimUnit c, int amount)
        {
            if (amount <= 0) return;
            c.Health -= amount;
            if (c.Health < 0) c.Health = 0;
        }

        // ---------- постройки ----------

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ActSite(int idx)
        {
            SimSite s = Sites[idx];
            switch (s.Structure)
            {
                case StructureType.Mine:
                {
                    int cash = s.Gold < 0 ? s.Rate : Math.Min(s.Rate, s.Gold);
                    if (Gold[s.Owner] >= 0) Gold[s.Owner] += cash;
                    if (s.Gold >= 0)
                    {
                        s.Gold -= cash;
                        if (s.Gold < 0) s.Gold = 0;
                        if (s.Gold <= 0) s.Clear();
                    }
                    Sites[idx] = s;
                    break;
                }
                case StructureType.Tower:
                {
                    int e = 1 - s.Owner;
                    if (TowerMeltFirst)
                    {
                        s.Hp -= Consts.TowerMeltRate;
                        s.AttackRadius = (int)Math.Sqrt((s.Hp * Consts.TowerCoveragePerHp + s.Area) / Math.PI);
                        if (s.Hp <= 0) { s.Clear(); Sites[idx] = s; break; }
                    }
                    double r2 = (double)(s.AttackRadius * s.AttackRadius);
                    int t = TowerTargetMode == 0 ? ClosestEnemyCreep(s.Owner, s.X, s.Y) : TowerTarget(s, e, r2);
                    if (t >= 0 && D2(Creeps[e][t].X, Creeps[e][t].Y, s.X, s.Y) < r2)
                    {
                        ref SimUnit c = ref Creeps[e][t];
                        double shot = Math.Sqrt(D2(c.X, c.Y, s.X, s.Y)) - s.Radius;
                        double diff = s.AttackRadius - shot;
                        if (TowerDamageMode == 1) DamageCreep(ref c, 5 + (int)(shot / Consts.TowerDamageClimbDistance));
                        else if (TowerDamageMode == 2) DamageCreep(ref c, 5 + (int)(diff / Consts.TowerDamageClimbDistance));
                        else if (TowerDamageMode == 3)
                        {
                            double sd = TowerDamageSubtractRadius ? shot : shot + s.Radius;
                            double x = TowerDamageUseDiff ? s.AttackRadius - sd : sd;
                            DamageCreep(ref c, TowerDamageMin + (int)(x / TowerDamageClimb));
                        }
                        else DamageCreep(ref c, Consts.TowerCreepDamageMin + (int)(diff / Consts.TowerDamageClimbDistance));
                    }
                    else if (D2(QueenX[e], QueenY[e], s.X, s.Y) < r2)
                    {
                        double shot = Math.Sqrt(D2(QueenX[e], QueenY[e], s.X, s.Y)) - s.Radius;
                        double diff = s.AttackRadius - shot;
                        int dmg;
                        if (TowerDamageMode == 1) dmg = Consts.TowerQueenDamageMin + (int)(shot / Consts.TowerDamageClimbDistance);
                        else if (TowerDamageMode == 3)
                        {
                            double sd = TowerDamageSubtractRadius ? shot : shot + s.Radius;
                            dmg = Consts.TowerQueenDamageMin + (int)((TowerDamageUseDiff ? s.AttackRadius - sd : sd) / TowerDamageClimb);
                        }
                        else dmg = Consts.TowerQueenDamageMin + (int)(diff / Consts.TowerDamageClimbDistance);
                        if (dmg > 0)
                        {
                            Health[e] -= dmg;
                            if (Health[e] < 0) Health[e] = 0;
                        }
                    }
                    if (!TowerMeltFirst)
                    {
                        s.Hp -= Consts.TowerMeltRate;
                        s.AttackRadius = (int)Math.Sqrt((s.Hp * Consts.TowerCoveragePerHp + s.Area) / Math.PI);
                        if (s.Hp <= 0) s.Clear();
                    }
                    Sites[idx] = s;
                    break;
                }
                case StructureType.Barracks:
                {
                    if (!s.Training) break;
                    s.Progress++;
                    if (s.Progress == CreepStats.BuildTime[s.CreepType])
                    {
                        s.Progress = 0;
                        s.Training = false;
                        Sites[idx] = s;
                        Spawn(idx);
                    }
                    else Sites[idx] = s;
                    break;
                }
            }
        }

        private static readonly int[] ArenaSpawnDx = { 1, -1, 1, -1 };
        private static readonly int[] ArenaSpawnDy = { -1, 1, 1, -1 };

        private void Spawn(int idx)
        {
            SimSite s = Sites[idx];
            int p = s.Owner, e = 1 - p, type = s.CreepType;
            int sign = p == 1 ? -1 : 1;
            for (int iter = 0; iter < CreepStats.Count[type]; iter++)
            {
                var c = new SimUnit { Type = type, Health = CreepStats.Hp[type], Radius = CreepStats.Radius[type], Mass = CreepStats.Mass[type] };
                if (ArenaSpawn)
                {
                    c.X = s.X; c.Y = s.Y;
                    Towards(ref c.X, ref c.Y, QueenX[e], QueenY[e], 30.0);
                    c.X += ArenaSpawnDx[iter & 3]; c.Y += ArenaSpawnDy[iter & 3];
                }
                else
                {
                    c.X = s.X + sign * iter;
                    c.Y = s.Y + sign * iter;
                    Towards(ref c.X, ref c.Y, QueenX[e], QueenY[e], 30.0);
                }
                AddCreep(p, c);
            }
            FixCollisions(999, true);
        }

        // ---------- конец хода ----------

        private void RemoveDead()
        {
            for (int p = 0; p < 2; p++)
            {
                int w = 0;
                for (int i = 0; i < CreepCount[p]; i++)
                    if (Creeps[p][i].Health != 0) Creeps[p][w++] = Creeps[p][i];
                CreepCount[p] = w;
            }
        }

        private void CheckEnd()
        {
            bool d0 = Health[0] == 0, d1 = Health[1] == 0;
            if (d0 || d1)
            {
                GameOver = true;
                Winner = d0 && d1 ? -1 : d0 ? 1 : 0;
            }
            else if (Turn + 1 >= Consts.MaxTurns)
            {
                GameOver = true;
                Winner = Health[0] > Health[1] ? 0 : Health[1] > Health[0] ? 1 : -1;
            }
        }

        private void Snap()
        {
            for (int p = 0; p < 2; p++)
            {
                for (int i = 0; i < CreepCount[p]; i++)
                {
                    Creeps[p][i].X = JavaMath.Round(Creeps[p][i].X);
                    Creeps[p][i].Y = JavaMath.Round(Creeps[p][i].Y);
                }
                QueenX[p] = JavaMath.Round(QueenX[p]);
                QueenY[p] = JavaMath.Round(QueenY[p]);
            }
        }

        // ---------- геометрия и коллизии (Vector2.kt, MapBuilding.kt) ----------

        private const double Eps2 = 1e-6 * 1e-6;

        /// <summary>Vector2.resizedTo: normalized * len; нулевой вектор нормализуется в (1, 0).</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void Resized(double vx, double vy, double len, out double rx, out double ry)
        {
            double l2 = vx * vx + vy * vy;
            double ux, uy;
            if (l2 < Eps2) { ux = 1; uy = 0; }
            else
            {
                double l = Math.Sqrt(l2);
                ux = vx / l; uy = vy / l;
            }
            rx = ux * len; ry = uy * len;
        }

        /// <summary>Vector2.towards: если цель ближе maxDist — встать в неё, иначе шаг maxDist к ней.</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static void Towards(ref double x, ref double y, double tx, double ty, double maxDist)
        {
            double dx = tx - x, dy = ty - y;
            if (dx * dx + dy * dy < maxDist * maxDist) { x = tx; y = ty; return; }
            double rx, ry;
            Resized(dx, dy, maxDist, out rx, out ry);
            x = x + rx; y = y + ry;
        }

        private int _nUnits;
        private int _sitesAt = -1, _sitesLen = -1;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void LoadBodies()
        {
            int n = CreepCount[0] + CreepCount[1] + 2 + Sites.Length;
            if (_bodies.Length < n) { _bodies = new Body[Math.Max(n, _bodies.Length * 2)]; _sitesAt = -1; }
            int k = 0;
            for (int p = 0; p < 2; p++)
            {
                if (InterleavedQueens && QueenFirst) _bodies[k++] = new Body { X = QueenX[p], Y = QueenY[p], Radius = Consts.QueenRadius, Mass = Consts.QueenMass };
                for (int i = 0; i < CreepCount[p]; i++)
                {
                    SimUnit c = Creeps[p][i];
                    _bodies[k++] = new Body { X = c.X, Y = c.Y, Radius = c.Radius, Mass = c.Mass };
                }
                if (InterleavedQueens && !QueenFirst) _bodies[k++] = new Body { X = QueenX[p], Y = QueenY[p], Radius = Consts.QueenRadius, Mass = Consts.QueenMass };
            }
            if (!InterleavedQueens)
                for (int p = 0; p < 2; p++) _bodies[k++] = new Body { X = QueenX[p], Y = QueenY[p], Radius = Consts.QueenRadius, Mass = Consts.QueenMass };
            _nUnits = k;
            // сайты не двигаются: их тела грузятся один раз на экземпляр (позиции/радиусы сайтов за партию не меняются),
            // но их место в массиве зависит от числа юнитов — перегружаем, только если сдвинулось начало блока сайтов
            if (_sitesAt != k || _sitesLen != Sites.Length)
            {
                for (int i = 0; i < Sites.Length; i++) _bodies[k + i] = new Body { X = Sites[i].X, Y = Sites[i].Y, Radius = Sites[i].Radius, Mass = 0 };
                _sitesAt = k; _sitesLen = Sites.Length;
            }
            _nBodies = k + Sites.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void StoreBodies()
        {
            int k = 0;
            for (int p = 0; p < 2; p++)
            {
                if (InterleavedQueens && QueenFirst) { QueenX[p] = _bodies[k].X; QueenY[p] = _bodies[k].Y; k++; }
                for (int i = 0; i < CreepCount[p]; i++)
                {
                    Creeps[p][i].X = _bodies[k].X; Creeps[p][i].Y = _bodies[k].Y; k++;
                }
                if (InterleavedQueens && !QueenFirst) { QueenX[p] = _bodies[k].X; QueenY[p] = _bodies[k].Y; k++; }
            }
            if (!InterleavedQueens)
                for (int p = 0; p < 2; p++) { QueenX[p] = _bodies[k].X; QueenY[p] = _bodies[k].Y; k++; }
        }

        // Списки соседей (широкая фаза): пары тел, которые могут пересечься за ход, в том же порядке обхода, что и полный проход.
        // За ход юнит смещается не больше своей скорости (≤ 100) плюс расталкивание, поэтому пары дальше суммы радиусов + NeighborMargin
        // не пересекутся ни на одном подшаге; результат бит-в-бит совпадает с полным проходом (проверяется фикстурами).
        private const double NeighborMargin = 320;
        private int[] _nbStart = new int[128], _nbCount = new int[128];
        private int[] _nb = new int[16384];

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void BuildNeighbors()
        {
            int n = _nBodies, nUnits = _nUnits;
            if (_nbStart.Length < n) { _nbStart = new int[n * 2]; _nbCount = new int[n * 2]; }
            if (_nb.Length < n * n) _nb = new int[n * n * 2];
            int nSites = n - nUnits;
            if (_siteNbCount.Length < nSites || _siteNb.Length < nSites * (nUnits + 1))
            {
                _siteNbCount = new int[Math.Max(nSites, _siteNbCount.Length)];
                _siteNb = new int[Math.Max(nSites * (nUnits + 1) * 2, _siteNb.Length)];
            }
            int stride = nUnits + 1;
            for (int sIdx = 0; sIdx < nSites; sIdx++) _siteNbCount[sIdx] = 0;
            int k = 0;
            for (int i = 0; i < nUnits; i++)
            {
                _nbStart[i] = k;
                double xi = _bodies[i].X, yi = _bodies[i].Y;
                int ri = _bodies[i].Radius;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    double dx = _bodies[j].X - xi, dy = _bodies[j].Y - yi;
                    double lim = ri + _bodies[j].Radius + NeighborMargin;
                    if (dx * dx + dy * dy < lim * lim)
                    {
                        _nb[k++] = j;
                        if (j >= nUnits) { int sIdx = j - nUnits; _siteNb[sIdx * stride + _siteNbCount[sIdx]++] = i; }   // юниты по возрастанию i
                    }
                }
                _nbCount[i] = k - _nbStart[i];
            }
            // строки сайтов: их соседи-юниты — зеркало строк юнитов (симметричное условие), уже по возрастанию
            for (int sIdx = 0; sIdx < nSites; sIdx++)
            {
                int i = nUnits + sIdx;
                _nbStart[i] = k;
                int cnt = _siteNbCount[sIdx];
                for (int q = 0; q < cnt; q++) _nb[k++] = _siteNb[sIdx * stride + q];
                _nbCount[i] = cnt;
            }
        }

        private int[] _siteNbCount = new int[32];
        private int[] _siteNb = new int[32 * 130];

        /// <summary>Индекс тела крипа (p, i) в порядке LoadBodies.</summary>
        private int BodyIndexOf(int p, int i)
        {
            int k = InterleavedQueens && QueenFirst ? 1 : 0;
            if (p == 1) k += CreepCount[0] + (InterleavedQueens ? 1 : 0);
            return k + i;
        }

        /// <summary>
        /// Ближайший сайт среди соседей тела (списки построены на подшаге 0 этого хода с запасом NeighborMargin):
        /// если глобально ближайший сайт может быть в касании, он в списке; если списка нет — касания нет. -1, если соседей-сайтов нет.
        /// </summary>
        private int ClosestSiteNear(int body, double x, double y)
        {
            int best = -1;
            double bestD = double.MaxValue;
            int qEnd = _nbStart[body] + _nbCount[body];
            for (int q = _nbStart[body]; q < qEnd; q++)
            {
                int j = _nb[q];
                if (j < _nUnits) continue;
                int sIdx = j - _nUnits;
                double d = D2(Sites[sIdx].X, Sites[sIdx].Y, x, y);
                if (d < bestD) { bestD = d; best = sIdx; }
            }
            return best;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void FixCollisions(int maxIterations, bool rebuildNeighbors)
        {
            long c0 = Profile ? Stopwatch.GetTimestamp() : 0;
            LoadBodies();
            long c1 = Profile ? Stopwatch.GetTimestamp() : 0;
            if (rebuildNeighbors) BuildNeighbors();
            long c2 = Profile ? Stopwatch.GetTimestamp() : 0;
            for (int it = 0; it < maxIterations; it++)
                if (!CollisionPass()) break;
            long c3 = Profile ? Stopwatch.GetTimestamp() : 0;
            StoreBodies();
            if (Profile) { long c4 = Stopwatch.GetTimestamp(); ProfLoad += c1 - c0; ProfBuild += c2 - c1; ProfPass += c3 - c2; ProfStore += c4 - c3; }
        }

        /// <summary>
        /// MapBuilding.collisionCheck с acceptableGap = 0: один проход по всем упорядоченным парам.
        /// Ускорения, не меняющие результат: сайты не клампятся и не проверяются друг с другом (они целые,
        /// в границах и разнесены при генерации карты), а sqrt считается только если квадрат расстояния меньше
        /// квадрата суммы радиусов — иначе overlap ≤ 0 и пара всё равно пропускается.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private bool CollisionPass()
        {
            int n = _nBodies, nUnits = _nUnits;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                bool site = i >= nUnits;
                if (!site)
                {
                    double clamp = _bodies[i].Radius;
                    double maxX = Consts.WorldWidth - clamp, maxY = Consts.WorldHeight - clamp;
                    double x = _bodies[i].X, y = _bodies[i].Y;
                    _bodies[i].X = x < clamp ? clamp : x > maxX ? maxX : x;
                    _bodies[i].Y = y < clamp ? clamp : y > maxY ? maxY : y;
                }
                double xi = _bodies[i].X, yi = _bodies[i].Y;
                int ri = _bodies[i].Radius;
                int qEnd = _nbStart[i] + _nbCount[i];
                for (int q = _nbStart[i]; q < qEnd; q++)
                {
                    int j = _nb[q];
                    double dx = _bodies[j].X - xi, dy = _bodies[j].Y - yi;
                    double d2 = dx * dx + dy * dy;
                    int rsum = ri + _bodies[j].Radius;
                    if (d2 >= (double)rsum * rsum) continue;
                    double dist = Math.Sqrt(d2);
                    double overlap = rsum + 0.0 - dist;
                    if (overlap <= 1e-6) continue;

                    int m1 = _bodies[i].Mass, m2 = _bodies[j].Mass;
                    double d1, dd2;
                    if (m1 == 0 && m2 == 0) { d1 = 0.5; dd2 = 0.5; }
                    else if (m1 == 0) { d1 = 0.0; dd2 = 1.0; }
                    else if (m2 == 0) { d1 = 1.0; dd2 = 0.0; }
                    else { d1 = (double)m2 / (m1 + m2); dd2 = (double)m1 / (m1 + m2); }
                    double gap = m1 == 0 && m2 == 0 ? 20.0 : 1.0;

                    double rx, ry;
                    Resized(dx, dy, d1 * overlap + (m1 == 0 && m2 > 0 ? 0.0 : gap), out rx, out ry);
                    _bodies[i].X -= rx; _bodies[i].Y -= ry;
                    xi = _bodies[i].X; yi = _bodies[i].Y;
                    Resized(dx, dy, dd2 * overlap + (m2 == 0 && m1 > 0 ? 0.0 : gap), out rx, out ry);
                    _bodies[j].X += rx; _bodies[j].Y += ry;
                    any = true;
                }
            }
            return any;
        }
    }
}
