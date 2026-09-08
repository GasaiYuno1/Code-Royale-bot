using System.IO;

namespace Royale.Tests
{
    public static class InputParserTests
    {
        [Test]
        public static void ParsesInitAndTurn()
        {
            var r = new StringReader(TestUtil.Init +
                "120 2\n" +
                "0 -1 -1 -1 -1 -1 -1\n" +
                "1 -1 -1 2 1 3 0\n" +
                "2 250 3 0 0 2 -1\n" +
                "3 -1 -1 1 1 400 512\n" +
                "3\n" +
                "500 300 0 0 30\n" +
                "200 200 0 -1 85\n" +
                "1720 800 1 -1 60\n");
            Site[] init = InputParser.ReadInit(r.ReadLine(), r);
            Assert.Equal(4, init.Length);
            Assert.Equal(1520, init[1].X);
            Assert.Equal(60, init[2].Radius);

            TurnInput t = InputParser.ReadTurn(r.ReadLine(), r, init);
            Assert.Equal(120, t.Gold);
            Assert.Equal(2, t.TouchedSite);
            Assert.True(t.Sites[0].IsEmpty);
            Assert.True(t.Sites[1].IsEnemy);
            Assert.Equal(StructureType.Barracks, t.Sites[1].Structure);
            Assert.Equal(3, t.Sites[1].Param1);
            Assert.True(t.Sites[2].IsOwnMine);
            Assert.Equal(250, t.Sites[2].Gold);
            Assert.Equal(2, t.Sites[2].Param1);
            Assert.Equal(2, t.Income);
            Assert.Equal(StructureType.Tower, t.Sites[3].Structure);
            Assert.Equal(512, t.Sites[3].Param2);
            Assert.Equal(3, t.Units.Length);
            Assert.Equal(UnitType.Knight, t.Units[0].Type);
            Assert.Equal(85, t.MyQueen.Health);
            Assert.Equal(1720, t.EnemyQueen.X);
            Assert.Equal(60, t.EnemyQueen.Health);
        }

        [Test]
        public static void SiteMemoryKeepsLastSeenGold()
        {
            var mem = new SiteMemory();
            var r = new StringReader(TestUtil.Init);
            Site[] init = InputParser.ReadInit(r.ReadLine(), r);

            var r1 = new StringReader(TestUtil.Turn(100,
                "0 230 2 -1 -1 -1 -1\n1 -1 -1 -1 -1 -1 -1\n2 -1 -1 -1 -1 -1 -1\n3 -1 -1 -1 -1 -1 -1\n"));
            TurnInput t1 = InputParser.ReadTurn(r1.ReadLine(), r1, init);
            mem.Apply(t1);
            Assert.Equal(230, t1.Sites[0].KnownGold);
            Assert.Equal(2, t1.Sites[0].KnownMaxMineSize);
            Assert.Equal(-1, t1.Sites[1].KnownGold);
            Assert.True(t1.Sites[1].MayHaveGold);

            var r2 = new StringReader(TestUtil.Turn(100));
            TurnInput t2 = InputParser.ReadTurn(r2.ReadLine(), r2, init);
            mem.Apply(t2);
            Assert.Equal(-1, t2.Sites[0].Gold);
            Assert.Equal(230, t2.Sites[0].KnownGold);
        }
    }
}
