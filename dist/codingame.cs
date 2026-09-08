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
public static class Creeps
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
private int[] _gold, _max;
public void Apply(TurnInput t)
{
if (_gold == null || _gold.Length != t.Sites.Length)
{
_gold = new int[t.Sites.Length];
_max = new int[t.Sites.Length];
for (int i = 0; i < _gold.Length; i++) { _gold[i] = -1; _max[i] = -1; }
}
for (int i = 0; i < t.Sites.Length; i++)
{
Site s = t.Sites[i];
if (s.Gold >= 0) _gold[i] = s.Gold;
if (s.MaxMineSize >= 0) _max[i] = s.MaxMineSize;
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
var bot = new Bot(strategy, stderr);
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
default: log.WriteLine("unknown key: " + key); break;
}
}
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
if (gold < Creeps.Cost[(int)UnitType.Knight]) break;
ids.Add(s.Id);
gold -= Creeps.Cost[(int)UnitType.Knight];
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
