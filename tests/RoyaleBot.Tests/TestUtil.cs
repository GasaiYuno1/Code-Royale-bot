using System.IO;
using System.Text;

namespace Royale.Tests
{
    public static class TestUtil
    {
        /// <summary>Стартовый ввод: 4 сайта, симметрично, как у рефери.</summary>
        public const string Init =
            "4\n" +
            "0 400 300 70\n" +
            "1 1520 700 70\n" +
            "2 900 200 60\n" +
            "3 1020 800 60\n";

        /// <summary>Ход: пустые сайты, обе королевы на старте, золото 100.</summary>
        public static string Turn(int gold, string sitesOverride = null)
        {
            string sites = sitesOverride ??
                "0 -1 -1 -1 -1 -1 -1\n" +
                "1 -1 -1 -1 -1 -1 -1\n" +
                "2 -1 -1 -1 -1 -1 -1\n" +
                "3 -1 -1 -1 -1 -1 -1\n";
            return gold + " -1\n" + sites + "2\n200 200 0 -1 100\n1720 800 1 -1 100\n";
        }

        public static string[] RunBot(IStrategy strategy, string input)
        {
            var output = new StringWriter();
            var bot = new Bot(strategy, TextWriter.Null);
            bot.Run(new StringReader(input), output);
            return output.ToString().TrimEnd().Split('\n');
        }
    }
}
