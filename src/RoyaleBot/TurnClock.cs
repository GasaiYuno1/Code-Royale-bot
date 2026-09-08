using System.Diagnostics;

namespace Royale
{
    /// <summary>Часы хода: запускаются сразу после получения первой строки ввода.</summary>
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
        // Значения рефери; запас на JIT/GC и задержки ввода-вывода.
        public const int FirstTurnMs = 1000;
        public const int TurnMs = 50;
        public const int SafetyMarginMs = 10;
    }
}
