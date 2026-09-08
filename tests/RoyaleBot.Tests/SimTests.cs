using System;
using System.Collections.Generic;

namespace Royale.Tests
{
    public static class SimTests
    {
        [Test]
        public static void JavaRound_HalfUp_LikeJvm()
        {
            Assert.Equal(1, JavaMath.Round(0.5));
            Assert.Equal(2, JavaMath.Round(1.5));
            Assert.Equal(3, JavaMath.Round(2.5));
            Assert.Equal(0, JavaMath.Round(-0.5));
            Assert.Equal(-1, JavaMath.Round(-1.5));
            Assert.Equal(-2, JavaMath.Round(-2.5));
            Assert.Equal(0, JavaMath.Round(0.49999999999999994));
            Assert.Equal(1234, JavaMath.Round(1233.5));
            Assert.Equal(1233, JavaMath.Round(1233.4999999));
            Assert.Equal(7, JavaMath.Round(7.0));
            Assert.Equal(0, JavaMath.Round(0.0));
        }

        [Test]
        public static void Towards_StopsAtTargetOrSteps()
        {
            double x = 0, y = 0;
            SimState.Towards(ref x, ref y, 30, 40, 60.0);
            Assert.Equal(30.0, x); Assert.Equal(40.0, y);
            x = 0; y = 0;
            SimState.Towards(ref x, ref y, 300, 400, 60.0);
            Assert.True(Math.Abs(x - 36) < 1e-9 && Math.Abs(y - 48) < 1e-9, x + "," + y);
            double rx, ry;
            SimState.Resized(0, 0, 3.0, out rx, out ry);
            Assert.Equal(3.0, rx); Assert.Equal(0.0, ry);
        }

        [Test]
        public static void TowerRadius_MatchesRefereeFormula()
        {
            // Referee: sqrt((health * 1000 + PI * r * r) / PI).toInt(); башня 196 HP на сайте r=70 -> 259
            double area = Math.PI * 70 * 70;
            Assert.Equal(259, (int)Math.Sqrt((196 * 1000 + area) / Math.PI));
        }

        [Test]
        public static void FromInputs_ThenToInputLines_RoundTrips()
        {
            var r = new System.IO.StringReader(TestUtil.Init);
            Site[] init = InputParser.ReadInit(r.ReadLine(), r);
            string sites = "0 230 2 2 0 0 0\n1 -1 -1 1 1 400 512\n2 -1 -1 -1 -1 -1 -1\n3 -1 -1 0 0 3 -1\n";
            var r0 = new System.IO.StringReader(TestUtil.Turn(120, sites));
            TurnInput t0 = InputParser.ReadTurn(r0.ReadLine(), r0, init);
            new SiteMemory().Apply(t0);
            SimState s = SimState.FromInputs(t0, null, 5);
            Assert.Equal(0, s.MyIndex);
            Assert.Equal(120, s.Gold[0]);
            Assert.Equal(-1, s.Gold[1]);
            Assert.Equal(StructureType.Tower, s.Sites[1].Structure);
            Assert.Equal(1, s.Sites[1].Owner);
            var lines = new List<string>();
            s.ToInputLines(0, lines);
            Assert.Equal("120 -1", lines[0]);
            Assert.Equal("0 230 2 2 0 0 0", lines[1]);
            Assert.Equal("1 -1 -1 1 1 400 512", lines[2]);
            Assert.Equal("3 ? ? 0 0 3 -1", lines[4]);
            Assert.Equal("2", lines[5]);
            Assert.Equal("200 200 0 -1 100", lines[6]);
            Assert.Equal("1720 800 1 -1 100", lines[7]);
        }

        [Test]
        public static void Step_MoveAndBuildBarracks_ThenTrainAndSpawn()
        {
            int saved = Rules.League;
            Rules.League = 4;
            try
            {
                var r = new System.IO.StringReader(TestUtil.Init);
                Site[] init = InputParser.ReadInit(r.ReadLine(), r);
                var r0 = new System.IO.StringReader(TestUtil.Turn(100));
                TurnInput t0 = InputParser.ReadTurn(r0.ReadLine(), r0, init);
                SimState s = SimState.FromInputs(t0, null, 0);
                s.Gold[1] = 100;
                // королева 0 идёт к сайту 0 (400,300) из (200,200): дистанция 224 > 60 → шаг 60
                s.Step(SimAction.Parse("BUILD 0 BARRACKS-KNIGHT", "TRAIN"), SimAction.Wait());
                Assert.True(s.QueenX[0] > 200 && s.QueenY[0] > 200, "queen moved " + s.QueenX[0] + "," + s.QueenY[0]);
                Assert.True(s.Sites[0].Structure == StructureType.None, "not yet built");
                for (int i = 0; i < 4 && s.Sites[0].Structure == StructureType.None; i++)
                    s.Step(SimAction.Parse("BUILD 0 BARRACKS-KNIGHT", "TRAIN"), SimAction.Wait());
                Assert.Equal(StructureType.Barracks, s.Sites[0].Structure);
                Assert.Equal(0, s.Sites[0].Owner);
                s.Step(SimAction.Parse("WAIT", "TRAIN 0"), SimAction.Wait());
                Assert.Equal(20, s.Gold[0]);
                Assert.True(s.Sites[0].Training);
                Assert.Equal(1, s.Sites[0].Progress);
                for (int i = 0; i < 4; i++) s.Step(SimAction.Wait(), SimAction.Wait());
                Assert.Equal(4, s.CreepCount[0]);
                Assert.False(s.Sites[0].Training);
                Assert.Equal(CreepStats.Hp[0], s.Creeps[0][0].Health);
                // на следующем ходу рыцари теряют 1 HP и идут к чужой королеве
                double x0 = s.Creeps[0][0].X;
                s.Step(SimAction.Wait(), SimAction.Wait());
                Assert.Equal(CreepStats.Hp[0] - 1, s.Creeps[0][0].Health);
                Assert.True(s.Creeps[0][0].X > x0, "knight advanced");
            }
            finally { Rules.League = saved; }
        }

        /// <summary>Логи tests/fixtures (tools/referee/match.sh ... -log): ни одного расхождения с рефери.</summary>
        [Test]
        public static void FixturesMatchReferee()
        {
            Replay.Stats st = Replay.Check(new[] { Replay.FixturesDir() }, 5);
            foreach (string e in st.Examples) Console.WriteLine("    " + e);
            Assert.True(st.Games >= 4, "fixtures: " + st.Games + " games");
            Assert.True(st.Turns >= 500, "turns " + st.Turns);
            Assert.Equal(0, st.BadLines);
        }
    }
}
