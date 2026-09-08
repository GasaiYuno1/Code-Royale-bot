using System.Reflection;

namespace Royale.Tests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "replay") return Replay.Run(args);
            if (args.Length > 0 && args[0] == "bench") return Bench.Run(args);
            string filter = args.Length > 0 ? args[0] : null;
            return Runner.RunAll(typeof(Program).Assembly, filter);
        }
    }
}
