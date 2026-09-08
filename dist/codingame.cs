using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System;

// ===== src/RoyaleBot/Bot.cs =====
namespace Royale
{
public sealed class Bot
{
private readonly IStrategy _strategy;
private readonly TextWriter _log;
private readonly SiteMemory _memory = new SiteMemory();
public Bot(IStrategy strategy, TextWriter log)
{
_strategy = strategy;
_log = log;
}
public void Run(TextReader input, TextWriter output)
{
string first = input.ReadLine();
if (first == null) return;
var clock = new TurnClock(TimeLimits.FirstTurnMs - TimeLimits.SafetyMarginMs);
Site[] sites;
try { sites = InputParser.ReadInit(first, input); }
catch (Exception e)
{
_log.WriteLine("Init error: " + e.Message);
sites = new Site[0];
}
int turn = 0;
while (true)
{
string line = input.ReadLine();
if (line == null) return;
if (turn > 0) clock = new TurnClock(TimeLimits.TurnMs - TimeLimits.SafetyMarginMs);
TurnOutput answer;
try
{
TurnInput t = InputParser.ReadTurn(line, input, sites);
_memory.Apply(t);
if (turn == 0)
{
try { _strategy.WarmUp(clock); }
catch (Exception e) { _log.WriteLine("WarmUp error: " + e.Message); }
}
answer = _strategy.Play(t, clock);
if (answer.Queen.Kind == QueenActionKind.Build && !QueenAction.Allowed(answer.Queen.Build)) answer.Queen = QueenAction.Wait();
}
catch (Exception e)
{
_log.WriteLine("Turn error: " + e.Message);
answer = TurnOutput.Idle;
}
string q = answer.Queen.Format(), tr = answer.TrainLine();
output.WriteLine(q);
output.WriteLine(tr);
output.Flush();
_log.WriteLine("turn " + turn + " in " + clock.ElapsedMs + " ms: " + q + " | " + tr);
turn++;
}
}
}
}

// ===== src/RoyaleBot/Game/Actions.cs =====
namespace Royale
{
public enum BuildType { Mine, Tower, BarracksKnight, BarracksArcher, BarracksGiant }
public enum QueenActionKind { Wait, Move, Build }
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

// ===== src/RoyaleBot/Game/Geom.cs =====
namespace Royale
{
public static class Geom
{
public static double Dist(int x1, int y1, int x2, int y2)
{
double dx = x1 - x2, dy = y1 - y2;
return Math.Sqrt(dx * dx + dy * dy);
}
public static long Dist2(int x1, int y1, int x2, int y2)
{
long dx = x1 - x2, dy = y1 - y2;
return dx * dx + dy * dy;
}
public static int Clamp(int v, int lo, int hi)
{
return v < lo ? lo : v > hi ? hi : v;
}
}
}

// ===== src/RoyaleBot/Game/InputParser.cs =====
namespace Royale
{
public static class InputParser
{
public static Site[] ReadInit(string first, TextReader r)
{
int n = int.Parse(first.Trim());
var sites = new Site[n];
for (int i = 0; i < n; i++)
{
string[] t = Split(ReadNonEmpty(r));
var s = new Site();
s.Id = int.Parse(t[0]);
s.X = int.Parse(t[1]);
s.Y = int.Parse(t[2]);
s.Radius = int.Parse(t[3]);
s.Gold = -1; s.MaxMineSize = -1; s.KnownGold = -1; s.KnownMaxMineSize = -1;
s.Structure = StructureType.None; s.Owner = -1; s.Param1 = -1; s.Param2 = -1;
sites[i] = s;
}
return sites;
}
public static TurnInput ReadTurn(string first, TextReader r, Site[] init)
{
string[] t = Split(first);
var turn = new TurnInput();
turn.Gold = int.Parse(t[0]);
turn.TouchedSite = int.Parse(t[1]);
turn.Sites = new Site[init.Length];
for (int i = 0; i < init.Length; i++)
{
t = Split(ReadNonEmpty(r));
int id = int.Parse(t[0]);
int idx = i;
if (init[idx].Id != id)
{
idx = -1;
for (int k = 0; k < init.Length; k++) if (init[k].Id == id) { idx = k; break; }
if (idx < 0) throw new FormatException("unknown site id " + id);
}
Site s = init[idx];
s.Gold = int.Parse(t[1]);
s.MaxMineSize = int.Parse(t[2]);
s.Structure = (StructureType)int.Parse(t[3]);
s.Owner = int.Parse(t[4]);
s.Param1 = int.Parse(t[5]);
s.Param2 = int.Parse(t[6]);
turn.Sites[idx] = s;
}
int m = int.Parse(ReadNonEmpty(r).Trim());
turn.Units = new UnitInfo[m];
for (int i = 0; i < m; i++)
{
t = Split(ReadNonEmpty(r));
var u = new UnitInfo();
u.X = int.Parse(t[0]);
u.Y = int.Parse(t[1]);
u.Owner = int.Parse(t[2]);
u.Type = (UnitType)int.Parse(t[3]);
u.Health = int.Parse(t[4]);
turn.Units[i] = u;
if (u.IsQueen)
{
if (u.Owner == 0) turn.MyQueen = u; else turn.EnemyQueen = u;
}
}
return turn;
}
private static string ReadNonEmpty(TextReader r)
{
while (true)
{
string line = r.ReadLine();
if (line == null) throw new EndOfStreamException("unexpected end of input");
if (line.Trim().Length > 0) return line;
}
}
private static string[] Split(string line)
{
return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
}
}
}

// ===== src/RoyaleBot/Game/Rules.cs =====
namespace Royale
{
public static class Rules
{
public const int DefaultLeague = 1;
public static int League = DefaultLeague;
public static bool Mines { get { return League >= 3; } }
public static bool Towers { get { return League >= 2; } }
public static bool Giants { get { return League >= 2; } }
public static bool FixedIncome { get { return League <= 2; } }
public static string Name
{
get { return League == 1 ? "Wood3" : League == 2 ? "Wood2" : League == 3 ? "Wood1" : "Bronze+"; }
}
}
public static class Consts
{
public const int WorldWidth = 1920;
public const int WorldHeight = 1000;
public const int StartingGold = 100;
public const int QueenSpeed = 60;
public const int QueenRadius = 30;
public const int QueenMass = 10000;
public const int QueenVision = 300;
public const int TouchingDelta = 5;
public const int TowerHpInitial = 200;
public const int TowerHpIncrement = 100;
public const int TowerHpMax = 800;
public const int TowerMeltRate = 4;
public const int TowerCoveragePerHp = 1000;
public const int TowerCreepDamageMin = 3;
public const int TowerQueenDamageMin = 1;
public const int TowerDamageClimbDistance = 200;
public const int GiantBustRate = 80;
public const int KnightDamage = 1;
public const int ArcherDamage = 2;
public const int ArcherDamageToGiants = 10;
public const int WoodFixedIncome = 10;
public const int MaxTurns = 200;
public const int ObstacleGap = 90;
}
public enum UnitType { Queen = -1, Knight = 0, Archer = 1, Giant = 2 }
public enum StructureType { None = -1, Mine = 0, Tower = 1, Barracks = 2 }
public static class CreepStats
{
public static readonly int[] Count = { 4, 2, 1 };
public static readonly int[] Cost = { 80, 100, 140 };
public static readonly int[] Speed = { 100, 75, 50 };
public static readonly int[] Range = { 0, 200, 0 };
public static readonly int[] Radius = { 20, 25, 40 };
public static readonly int[] Mass = { 400, 900, 2000 };
public static readonly int[] Hp = { 30, 45, 200 };
public static readonly int[] BuildTime = { 5, 8, 10 };
public static readonly string[] Name = { "KNIGHT", "ARCHER", "GIANT" };
}
}

// ===== src/RoyaleBot/Game/Site.cs =====
namespace Royale
{
public struct Site
{
public int Id, X, Y, Radius;
public int Gold, MaxMineSize;
public int KnownGold, KnownMaxMineSize;
public StructureType Structure;
public int Owner;
public int Param1;
public int Param2;
public bool IsEmpty { get { return Structure == StructureType.None; } }
public bool IsOwn { get { return Owner == 0 && Structure != StructureType.None; } }
public bool IsEnemy { get { return Owner == 1 && Structure != StructureType.None; } }
public bool IsOwnTower { get { return IsOwn && Structure == StructureType.Tower; } }
public bool IsOwnMine { get { return IsOwn && Structure == StructureType.Mine; } }
public bool BarracksIdle { get { return Structure == StructureType.Barracks && Param1 == 0; } }
public bool MayHaveGold { get { return KnownGold != 0; } }
public bool IsOwnBarracks(UnitType creep)
{
return IsOwn && Structure == StructureType.Barracks && Param2 == (int)creep;
}
public override string ToString()
{
return "site " + Id + " (" + X + "," + Y + ") r" + Radius + " " + Structure + " owner " + Owner + " p1 " + Param1 + " p2 " + Param2 + " gold " + Gold + "/" + KnownGold;
}
}
}

// ===== src/RoyaleBot/Game/SiteMemory.cs =====
namespace Royale
{
public sealed class SiteMemory
{
private int[] _gold, _max, _rate;
private bool[] _wasMine;
public void Apply(TurnInput t)
{
int n = t.Sites.Length;
if (_gold == null || _gold.Length != n)
{
_gold = new int[n]; _max = new int[n]; _rate = new int[n]; _wasMine = new bool[n];
for (int i = 0; i < n; i++) { _gold[i] = -1; _max[i] = -1; }
}
for (int i = 0; i < n; i++)
{
Site s = t.Sites[i];
bool isMine = s.Structure == StructureType.Mine;
if (s.Gold >= 0) _gold[i] = s.Gold;
else if (_wasMine[i] && _gold[i] >= 0 && _rate[i] > 0)
{
if (s.Structure == StructureType.None) { if (_gold[i] <= _rate[i]) _gold[i] = 0; }
else if (isMine) { _gold[i] -= _rate[i]; if (_gold[i] < 0) _gold[i] = 0; }
}
if (s.MaxMineSize >= 0) _max[i] = s.MaxMineSize;
if (isMine) { if (s.Param1 >= 0) _rate[i] = s.Param1; }
else _rate[i] = 0;
_wasMine[i] = isMine;
s.KnownGold = _gold[i];
s.KnownMaxMineSize = _max[i];
t.Sites[i] = s;
}
}
}
}

// ===== src/RoyaleBot/Game/TurnInput.cs =====
namespace Royale
{
public sealed class TurnInput
{
public int Gold;
public int TouchedSite;
public Site[] Sites;
public UnitInfo[] Units;
public UnitInfo MyQueen, EnemyQueen;
public int Income
{
get
{
int s = 0;
foreach (Site site in Sites) if (site.IsOwnMine && site.Param1 > 0) s += site.Param1;
return s;
}
}
public int CountOwn(StructureType t)
{
int n = 0;
foreach (Site site in Sites) if (site.IsOwn && site.Structure == t) n++;
return n;
}
public int IndexOfSite(int id)
{
if (id >= 0 && id < Sites.Length && Sites[id].Id == id) return id;
for (int i = 0; i < Sites.Length; i++) if (Sites[i].Id == id) return i;
return -1;
}
}
}

// ===== src/RoyaleBot/Game/UnitInfo.cs =====
namespace Royale
{
public struct UnitInfo
{
public int X, Y;
public int Owner;
public UnitType Type;
public int Health;
public bool IsQueen { get { return Type == UnitType.Queen; } }
public bool IsOwn { get { return Owner == 0; } }
public override string ToString()
{
return Type + " (" + X + "," + Y + ") owner " + Owner + " hp " + Health;
}
}
}

// ===== src/RoyaleBot/Program.cs =====
namespace Royale
{
public static class Program
{
public static void Main(string[] args)
{
var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = false };
var stderr = Console.Error;
var strategy = new WoodStrategy();
Tuning.Apply(args, strategy, stderr);
stderr.WriteLine("league " + Rules.League + " (" + Rules.Name + ")");
IStrategy chosen = strategy;
foreach (string a in args) if (a.StartsWith("random=")) chosen = new RandomStrategy(int.Parse(a.Substring(7)));
var bot = new Bot(chosen, stderr);
bot.Run(Console.In, stdout);
}
}
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
case "reach": s.TowerReach = v; break;
case "random": break;
default: log.WriteLine("unknown key: " + key); break;
}
}
}
}
}

// ===== src/RoyaleBot/Sim/JavaMath.cs =====
namespace Royale
{
public static class JavaMath
{
public static int Round(double a)
{
long bits = BitConverter.DoubleToInt64Bits(a);
long biasedExp = (bits & 0x7FF0000000000000L) >> 52;
long shift = (52 - 1 + 1023) - biasedExp;
if ((shift & -64) == 0)
{
long r = (bits & 0x000FFFFFFFFFFFFFL) | 0x0010000000000000L;
if (bits < 0) r = -r;
return (int)(((r >> (int)shift) + 1) >> 1);
}
return (int)(long)a;
}
}
}

// ===== src/RoyaleBot/Sim/SimAction.cs =====
namespace Royale
{
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

// ===== src/RoyaleBot/Sim/SimState.cs =====
namespace Royale
{
public struct SimSite
{
public int Id, X, Y, Radius;
public double Area;
public int Gold;
public int MaxMineSize;
public StructureType Structure;
public int Owner;
public int Rate;
public int Hp, AttackRadius;
public int CreepType, Progress;
public bool Training;
public void Clear()
{
Structure = StructureType.None; Owner = -1; Rate = 0; Hp = 0; AttackRadius = 0; CreepType = 0; Progress = 0; Training = false;
}
}
public struct SimUnit
{
public double X, Y;
public int Type;
public int Health;
public int Radius, Mass;
}
public sealed partial class SimState
{
public int Turn;
public int MyIndex;
public SimSite[] Sites;
public SimUnit[][] Creeps = new SimUnit[2][];
public int[] CreepCount = new int[2];
public double[] QueenX = new double[2], QueenY = new double[2];
public int[] Health = new int[2];
public int[] Gold = new int[2];
public bool GameOver;
public int Winner = -1;
public bool[] Killed = new bool[2];
public SimState()
{
Creeps[0] = new SimUnit[32];
Creeps[1] = new SimUnit[32];
}
public int OtherIndex { get { return 1 - MyIndex; } }
public static SimState FromInputs(TurnInput mine, TurnInput other, int turn)
{
return FromInputs(mine, other, turn, null);
}
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

// ===== src/RoyaleBot/Sim/SimStep.cs =====
namespace Royale
{
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
public void Step(SimAction a0, SimAction a1)
{
if (GameOver) return;
ProcessPlayerActions(a0, a1);
if (GameOver) { Turn++; return; }
ProcessCreeps();
for (int i = 0; i < Sites.Length; i++) ActSite(i);
if (Rules.FixedIncome)
{
Gold[0] += Consts.WoodFixedIncome;
Gold[1] += Consts.WoodFixedIncome;
}
RemoveDead();
CheckEnd();
Snap();
Turn++;
}
private void ProcessPlayerActions(SimAction a0, SimAction a1)
{
int nAttempted = 0, nSched = 0;
for (int p = 0; p < 2; p++)
{
SimAction a = p == 0 ? a0 : a1;
if (a.Invalid) { Kill(p); continue; }
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
if (s.Structure != StructureType.None && s.Owner == 1 - p) break;
if (s.Structure == StructureType.Barracks && s.Owner == p && s.Training) break;
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
private int SiteIndex(int id)
{
if (id >= 0 && id < Sites.Length && Sites[id].Id == id) return id;
for (int i = 0; i < Sites.Length; i++) if (Sites[i].Id == id) return i;
return -1;
}
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
private void ProcessCreeps()
{
BuildOrder();
for (int sub = 0; sub < 5; sub++)
{
for (int k = 0; k < _nOrder; k++) MoveCreep(_order[k].P, _order[k].I);
FixCollisions(1);
}
for (int k = 0; k < _nOrder; k++) DealDamage(_order[k].P, _order[k].I);
for (int k = 0; k < _nOrder; k++)
{
SimUnit c = Creeps[_order[k].P][_order[k].I];
int idx = ClosestSite(c.X, c.Y);
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
for (int p = 0; p < 2; p++)
{
int idx = ClosestSite(QueenX[p], QueenY[p]);
int lim = Sites[idx].Radius + Consts.QueenRadius + Consts.TouchingDelta;
if (D2(Sites[idx].X, Sites[idx].Y, QueenX[p], QueenY[p]) >= (double)(lim * lim)) continue;
StructureType st = Sites[idx].Structure;
if ((st == StructureType.Mine || st == StructureType.Barracks) && Sites[idx].Owner != p) Sites[idx].Clear();
}
}
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
private void MoveCreep(int p, int i)
{
ref SimUnit c = ref Creeps[p][i];
int e = 1 - p;
switch (c.Type)
{
case 0:
{
double qx = QueenX[e], qy = QueenY[e];
int lim = c.Radius + Consts.QueenRadius + CreepStats.Range[0];
if (D2(c.X, c.Y, qx, qy) > (double)(lim * lim))
{
double rx, ry;
Resized(c.X - qx, c.Y - qy, 3.0, out rx, out ry);
Towards(ref c.X, ref c.Y, qx + rx, qy + ry, (double)CreepStats.Speed[0] * Frame);
}
break;
}
case 1:
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
default:
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
int t = ClosestEnemyCreep(s.Owner, s.X, s.Y);
double r2 = (double)(s.AttackRadius * s.AttackRadius);
if (t >= 0 && D2(Creeps[e][t].X, Creeps[e][t].Y, s.X, s.Y) < r2)
{
ref SimUnit c = ref Creeps[e][t];
double shot = Math.Sqrt(D2(c.X, c.Y, s.X, s.Y)) - s.Radius;
double diff = s.AttackRadius - shot;
DamageCreep(ref c, Consts.TowerCreepDamageMin + (int)(diff / Consts.TowerDamageClimbDistance));
}
else if (D2(QueenX[e], QueenY[e], s.X, s.Y) < r2)
{
double shot = Math.Sqrt(D2(QueenX[e], QueenY[e], s.X, s.Y)) - s.Radius;
double diff = s.AttackRadius - shot;
int dmg = Consts.TowerQueenDamageMin + (int)(diff / Consts.TowerDamageClimbDistance);
if (dmg > 0)
{
Health[e] -= dmg;
if (Health[e] < 0) Health[e] = 0;
}
}
s.Hp -= Consts.TowerMeltRate;
s.AttackRadius = (int)Math.Sqrt((s.Hp * Consts.TowerCoveragePerHp + s.Area) / Math.PI);
if (s.Hp <= 0) s.Clear();
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
private void Spawn(int idx)
{
SimSite s = Sites[idx];
int p = s.Owner, e = 1 - p, type = s.CreepType;
int sign = p == 1 ? -1 : 1;
for (int iter = 0; iter < CreepStats.Count[type]; iter++)
{
var c = new SimUnit { Type = type, Health = CreepStats.Hp[type], Radius = CreepStats.Radius[type], Mass = CreepStats.Mass[type] };
c.X = s.X + sign * iter;
c.Y = s.Y + sign * iter;
Towards(ref c.X, ref c.Y, QueenX[e], QueenY[e], 30.0);
AddCreep(p, c);
}
FixCollisions(999);
}
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
private const double Eps2 = 1e-6 * 1e-6;
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
public static void Towards(ref double x, ref double y, double tx, double ty, double maxDist)
{
double dx = tx - x, dy = ty - y;
if (dx * dx + dy * dy < maxDist * maxDist) { x = tx; y = ty; return; }
double rx, ry;
Resized(dx, dy, maxDist, out rx, out ry);
x = x + rx; y = y + ry;
}
private int _nUnits;
private void LoadBodies()
{
int n = CreepCount[0] + CreepCount[1] + 2 + Sites.Length;
if (_bodies.Length < n) _bodies = new Body[Math.Max(n, _bodies.Length * 2)];
int k = 0;
for (int p = 0; p < 2; p++)
for (int i = 0; i < CreepCount[p]; i++)
{
SimUnit c = Creeps[p][i];
_bodies[k++] = new Body { X = c.X, Y = c.Y, Radius = c.Radius, Mass = c.Mass };
}
for (int p = 0; p < 2; p++) _bodies[k++] = new Body { X = QueenX[p], Y = QueenY[p], Radius = Consts.QueenRadius, Mass = Consts.QueenMass };
_nUnits = k;
for (int i = 0; i < Sites.Length; i++) _bodies[k++] = new Body { X = Sites[i].X, Y = Sites[i].Y, Radius = Sites[i].Radius, Mass = 0 };
_nBodies = k;
}
private void StoreBodies()
{
int k = 0;
for (int p = 0; p < 2; p++)
for (int i = 0; i < CreepCount[p]; i++)
{
Creeps[p][i].X = _bodies[k].X; Creeps[p][i].Y = _bodies[k].Y; k++;
}
for (int p = 0; p < 2; p++) { QueenX[p] = _bodies[k].X; QueenY[p] = _bodies[k].Y; k++; }
}
private void FixCollisions(int maxIterations)
{
LoadBodies();
for (int it = 0; it < maxIterations; it++)
if (!CollisionPass()) break;
StoreBodies();
}
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
int jEnd = site ? nUnits : n;
double xi = _bodies[i].X, yi = _bodies[i].Y;
int ri = _bodies[i].Radius;
for (int j = 0; j < jEnd; j++)
{
if (j == i) continue;
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

// ===== src/RoyaleBot/Strategy/IStrategy.cs =====
namespace Royale
{
public interface IStrategy
{
void WarmUp(TurnClock clock);
TurnOutput Play(TurnInput turn, TurnClock clock);
}
}

// ===== src/RoyaleBot/Strategy/RandomStrategy.cs =====
namespace Royale
{
public sealed class RandomStrategy : IStrategy
{
private readonly Random _rnd;
public RandomStrategy(int seed) { _rnd = new Random(seed); }
public void WarmUp(TurnClock clock) { }
public TurnOutput Play(TurnInput t, TurnClock clock)
{
var o = new TurnOutput { Queen = QueenAction.Wait(), Train = new int[0] };
var ids = new List<int>();
int gold = t.Gold;
foreach (Site s in t.Sites)
{
if (!s.IsOwn || s.Structure != StructureType.Barracks || !s.BarracksIdle) continue;
if (_rnd.NextDouble() < 0.3) continue;
int cost = CreepStats.Cost[s.Param2];
if (gold < cost) continue;
ids.Add(s.Id);
gold -= cost;
}
o.Train = ids.ToArray();
int r = _rnd.Next(100);
if (r < 10) return o;
if (r < 35)
{
o.Queen = QueenAction.Move(_rnd.Next(Consts.WorldWidth + 1), _rnd.Next(Consts.WorldHeight + 1));
return o;
}
var allowed = new List<BuildType>();
allowed.Add(BuildType.BarracksKnight);
allowed.Add(BuildType.BarracksArcher);
if (Rules.Giants) allowed.Add(BuildType.BarracksGiant);
if (Rules.Towers) { allowed.Add(BuildType.Tower); allowed.Add(BuildType.Tower); }
if (Rules.Mines) { allowed.Add(BuildType.Mine); allowed.Add(BuildType.Mine); }
UnitInfo q = t.MyQueen;
int idx;
if (_rnd.Next(100) < 70)
{
var near = new List<int>();
for (int i = 0; i < t.Sites.Length; i++) near.Add(i);
near.Sort((x, y) => Geom.Dist2(t.Sites[x].X, t.Sites[x].Y, q.X, q.Y).CompareTo(Geom.Dist2(t.Sites[y].X, t.Sites[y].Y, q.X, q.Y)));
idx = near[_rnd.Next(Math.Min(5, near.Count))];
}
else idx = _rnd.Next(t.Sites.Length);
o.Queen = QueenAction.BuildAt(t.Sites[idx].Id, allowed[_rnd.Next(allowed.Count)]);
return o;
}
}
}

// ===== src/RoyaleBot/Strategy/WoodStrategy.cs =====
namespace Royale
{
public sealed class WoodStrategy : IStrategy
{
public int TargetIncome = 5;
public int TargetTowers = 3;
public int TowerUpgradeBelow = 500;
public int DangerRadius = 500;
public int TowerReach = 700;
public int EnemyZone = 350;
public int KnightZone = 250;
private int _homeX = -1, _homeY = -1;
public void WarmUp(TurnClock clock) { }
public TurnOutput Play(TurnInput t, TurnClock clock)
{
UnitInfo q = t.MyQueen;
if (_homeX < 0) { _homeX = q.X; _homeY = q.Y; }
var o = new TurnOutput { Queen = QueenAction.Wait(), Train = ChooseTrain(t) };
int kx = 0, ky = 0, kn = 0;
foreach (UnitInfo u in t.Units)
{
if (u.Owner != 1 || u.Type != UnitType.Knight) continue;
if (Geom.Dist(u.X, u.Y, q.X, q.Y) < DangerRadius) { kx += u.X; ky += u.Y; kn++; }
}
if (kn > 0)
{
o.Queen = Retreat(t, q, kx / kn, ky / kn);
return o;
}
BuildType type;
int idx = ChooseBuild(t, out type);
if (idx >= 0 && QueenAction.Allowed(type)) o.Queen = QueenAction.BuildAt(t.Sites[idx].Id, type);
return o;
}
private QueenAction Retreat(TurnInput t, UnitInfo q, int cx, int cy)
{
if (Rules.Towers)
{
int best = -1;
double bestD = TowerReach;
for (int i = 0; i < t.Sites.Length; i++)
{
Site s = t.Sites[i];
if (!s.IsOwnTower) continue;
double d = Geom.Dist(s.X, s.Y, q.X, q.Y);
if (d < bestD) { bestD = d; best = i; }
}
if (best >= 0) return QueenAction.BuildAt(t.Sites[best].Id, BuildType.Tower);
best = -1; bestD = TowerReach;
for (int i = 0; i < t.Sites.Length; i++)
{
Site s = t.Sites[i];
if (!s.IsEmpty) continue;
double d = Geom.Dist(s.X, s.Y, q.X, q.Y);
if (d >= bestD) continue;
if (Geom.Dist(s.X, s.Y, cx, cy) < Geom.Dist(q.X, q.Y, cx, cy)) continue;
bestD = d; best = i;
}
if (best >= 0) return QueenAction.BuildAt(t.Sites[best].Id, BuildType.Tower);
}
double ax = q.X - cx, ay = q.Y - cy;
double al = Math.Sqrt(ax * ax + ay * ay);
if (al < 1e-6) { ax = _homeX - q.X; ay = _homeY - q.Y; al = Math.Sqrt(ax * ax + ay * ay); }
if (al < 1e-6) { ax = 1; ay = 0; al = 1; }
ax /= al; ay /= al;
double hx = _homeX - q.X, hy = _homeY - q.Y;
double hl = Math.Sqrt(hx * hx + hy * hy);
if (hl > 1e-6) { hx /= hl; hy /= hl; } else { hx = 0; hy = 0; }
double dx = ax * 0.7 + hx * 0.3, dy = ay * 0.7 + hy * 0.3;
double dl = Math.Sqrt(dx * dx + dy * dy);
if (dl < 1e-6) { dx = ax; dy = ay; dl = 1; }
dx /= dl; dy /= dl;
int tx = Geom.Clamp((int)Math.Round(q.X + dx * 300), Consts.QueenRadius, Consts.WorldWidth - Consts.QueenRadius);
int ty = Geom.Clamp((int)Math.Round(q.Y + dy * 300), Consts.QueenRadius, Consts.WorldHeight - Consts.QueenRadius);
return QueenAction.Move(tx, ty);
}
private enum Filter { Empty, EmptyWithGold, OwnMineUpgradable }
private int ChooseBuild(TurnInput t, out BuildType type)
{
type = BuildType.BarracksKnight;
int knightBarracks = 0, towers = 0;
foreach (Site s in t.Sites)
{
if (s.IsOwnBarracks(UnitType.Knight)) knightBarracks++;
if (s.IsOwnTower) towers++;
}
if (knightBarracks == 0)
{
int i = Nearest(t, Filter.Empty);
if (i >= 0) { type = BuildType.BarracksKnight; return i; }
}
if (Rules.Mines && t.Income < TargetIncome)
{
int i = Nearest(t, Filter.OwnMineUpgradable);
if (i < 0) i = Nearest(t, Filter.EmptyWithGold);
if (i >= 0) { type = BuildType.Mine; return i; }
}
if (Rules.Towers)
{
if (towers < TargetTowers)
{
int i = Nearest(t, Filter.Empty);
if (i >= 0) { type = BuildType.Tower; return i; }
}
int weakest = -1, hp = int.MaxValue;
for (int i = 0; i < t.Sites.Length; i++)
{
Site s = t.Sites[i];
if (s.IsOwnTower && s.Param1 < TowerUpgradeBelow && s.Param1 < hp) { weakest = i; hp = s.Param1; }
}
if (weakest >= 0) { type = BuildType.Tower; return weakest; }
}
if (Rules.Mines && t.Income < TargetIncome + 3)
{
int i = Nearest(t, Filter.OwnMineUpgradable);
if (i < 0) i = Nearest(t, Filter.EmptyWithGold);
if (i >= 0) { type = BuildType.Mine; return i; }
}
return -1;
}
private int Nearest(TurnInput t, Filter f)
{
UnitInfo q = t.MyQueen, eq = t.EnemyQueen;
int best = -1;
double bestD = double.MaxValue;
for (int i = 0; i < t.Sites.Length; i++)
{
Site s = t.Sites[i];
bool ok;
switch (f)
{
case Filter.Empty: ok = s.IsEmpty; break;
case Filter.EmptyWithGold: ok = s.IsEmpty && s.MayHaveGold; break;
default: ok = s.IsOwnMine && s.KnownMaxMineSize > 0 && s.Param1 < s.KnownMaxMineSize && s.MayHaveGold; break;
}
if (!ok) continue;
if (Geom.Dist(s.X, s.Y, eq.X, eq.Y) < EnemyZone) continue;
if (KnightNear(t, s.X, s.Y, KnightZone)) continue;
double d = Geom.Dist(s.X, s.Y, q.X, q.Y);
if (d < bestD) { bestD = d; best = i; }
}
return best;
}
private static bool KnightNear(TurnInput t, int x, int y, int radius)
{
foreach (UnitInfo u in t.Units)
if (u.Owner == 1 && u.Type == UnitType.Knight && Geom.Dist(u.X, u.Y, x, y) < radius) return true;
return false;
}
private int[] ChooseTrain(TurnInput t)
{
var ids = new List<int>();
int gold = t.Gold;
foreach (Site s in t.Sites)
{
if (!s.IsOwnBarracks(UnitType.Knight) || !s.BarracksIdle) continue;
if (gold < CreepStats.Cost[(int)UnitType.Knight]) break;
ids.Add(s.Id);
gold -= CreepStats.Cost[(int)UnitType.Knight];
}
return ids.ToArray();
}
}
}

// ===== src/RoyaleBot/TurnClock.cs =====
namespace Royale
{
public sealed class TurnClock
{
private readonly Stopwatch _sw = Stopwatch.StartNew();
public readonly int BudgetMs;
public TurnClock(int budgetMs) { BudgetMs = budgetMs; }
public long ElapsedMs { get { return _sw.ElapsedMilliseconds; } }
public long RemainingMs { get { return BudgetMs - _sw.ElapsedMilliseconds; } }
public bool TimeUp { get { return _sw.ElapsedMilliseconds >= BudgetMs; } }
}
public static class TimeLimits
{
public const int FirstTurnMs = 1000;
public const int TurnMs = 50;
public const int SafetyMarginMs = 10;
}
}
