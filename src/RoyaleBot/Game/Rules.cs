using System;

namespace Royale
{
    /// <summary>
    /// Лига, под которую собран бот. Из ввода лига не определяется, а BUILD постройки, отключённой в лиге,
    /// убивает бота (PlayerInputException в рефери). Перед вставкой на CodinGame выставить DefaultLeague.
    /// 1 = Wood 3, 2 = Wood 2, 3 = Wood 1, 4 и выше = Bronze и дальше (полные правила).
    /// </summary>
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

    /// <summary>Константы рефери (Constants.kt).</summary>
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
        public const int MaxTurns = 250;         // на арене 250 ходов (501 кадр реплея); в исходниках рефери на GitHub — 200
        public const int ObstacleGap = 90;
    }

    public enum UnitType { Queen = -1, Knight = 0, Archer = 1, Giant = 2 }

    public enum StructureType { None = -1, Mine = 0, Tower = 1, Barracks = 2 }

    /// <summary>Параметры крипов по типу (индекс = UnitType): CreepType в Constants.kt.</summary>
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
