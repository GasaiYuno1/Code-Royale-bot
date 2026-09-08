using System.Text.RegularExpressions;

namespace Royale.Tests
{
    public static class BotTests
    {
        private static readonly Regex QueenLine = new Regex(@"^(WAIT|MOVE \d+ \d+|BUILD \d+ (MINE|TOWER|BARRACKS-(KNIGHT|ARCHER|GIANT)))$");
        private static readonly Regex TrainLine = new Regex(@"^TRAIN( \d+)*$");

        [Test]
        public static void TwoLinesPerTurn_ValidSyntax()
        {
            string[] lines = TestUtil.RunBot(new WoodStrategy(), TestUtil.Init + TestUtil.Turn(100) + TestUtil.Turn(110));
            Assert.Equal(4, lines.Length);
            for (int i = 0; i < 4; i += 2)
            {
                Assert.True(QueenLine.IsMatch(lines[i].Trim()), "queen line: " + lines[i]);
                Assert.True(TrainLine.IsMatch(lines[i + 1].Trim()), "train line: " + lines[i + 1]);
            }
        }

        [Test]
        public static void FirstMove_BuildsKnightBarracksOnNearestSite()
        {
            string[] lines = TestUtil.RunBot(new WoodStrategy(), TestUtil.Init + TestUtil.Turn(100));
            Assert.Equal("BUILD 0 BARRACKS-KNIGHT", lines[0].Trim());
            Assert.Equal("TRAIN", lines[1].Trim());
        }

        [Test]
        public static void TrainsFromIdleKnightBarracksWhenGoldAllows()
        {
            string sites = "0 -1 -1 2 0 0 0\n1 -1 -1 -1 -1 -1 -1\n2 -1 -1 -1 -1 -1 -1\n3 -1 -1 -1 -1 -1 -1\n";
            string[] lines = TestUtil.RunBot(new WoodStrategy(), TestUtil.Init + TestUtil.Turn(95, sites) + TestUtil.Turn(70, sites));
            Assert.Equal("TRAIN 0", lines[1].Trim());
            Assert.Equal("TRAIN", lines[3].Trim());
        }

        [Test]
        public static void Wood3_NeverBuildsDisabledStructures()
        {
            int saved = Rules.League;
            Rules.League = 1;
            try
            {
                string sites = "0 -1 -1 2 0 0 0\n1 -1 -1 -1 -1 -1 -1\n2 -1 -1 -1 -1 -1 -1\n3 -1 -1 -1 -1 -1 -1\n";
                var sb = new System.Text.StringBuilder(TestUtil.Init);
                for (int i = 0; i < 20; i++) sb.Append(TestUtil.Turn(100 + i * 10, sites));
                string[] lines = TestUtil.RunBot(new WoodStrategy(), sb.ToString());
                foreach (string l in lines)
                {
                    Assert.False(l.Contains("TOWER") || l.Contains("MINE") || l.Contains("GIANT"), "wood3 line: " + l);
                }
            }
            finally { Rules.League = saved; }
        }

        [Test]
        public static void BrokenInput_StillAnswersTwoLines()
        {
            string[] lines = TestUtil.RunBot(new WoodStrategy(), "garbage\nmore garbage\n");
            Assert.Equal(2, lines.Length);
            Assert.Equal("WAIT", lines[0].Trim());
            Assert.Equal("TRAIN", lines[1].Trim());
        }
    }
}
