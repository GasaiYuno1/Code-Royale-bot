using System;
using System.IO;

namespace Royale
{
    /// <summary>
    /// Оркестратор: читает стартовый ввод и ходы, вызывает стратегию, печатает две строки ответа.
    /// Не зависит от консоли — принимает TextReader/TextWriter, чтобы гоняться в тестах и локальном арбитре.
    /// Любая ошибка на ходу превращается в WAIT / TRAIN: синтаксически кривой ответ = поражение.
    /// </summary>
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
