namespace Royale
{
    public interface IStrategy
    {
        /// <summary>Вызывается на первом ходу (бюджет 1000 мс): прогрев JIT тяжёлого кода.</summary>
        void WarmUp(TurnClock clock);

        TurnOutput Play(TurnInput turn, TurnClock clock);
    }
}
