using System.Text;
using NUnit.Framework;

namespace GoodEnoughRepairs.Tests
{
    public class RunTests
    {
        private static BalanceData MakeBalance(int credits, int upkeep, int price, int ships)
        {
            DayBalance[] days = new DayBalance[7];
            for (int i = 0; i < days.Length; i++)
            {
                days[i] = new DayBalance
                {
                    day = i + 1,
                    type = "quiet",
                    ships = ships,
                    junkParts = 7,
                    weakPartsMin = 1,
                    weakPartsMax = 1,
                    fighterWeakConditionMin = 62,
                    fighterWeakConditionMax = 69,
                    freighterWeakConditionMin = 45,
                    freighterWeakConditionMax = 55,
                    healthyConditionMin = 80,
                    healthyConditionMax = 100
                };
            }
            return new BalanceData
            {
                startingCredits = credits,
                newPartPrice = price,
                penaltyPercent = 20,
                scrapPrice = 10,
                upkeep = upkeep,
                playerActionMs = 3000,
                shipTurnoverMs = 5000,
                dayStartLandingMs = 2500,
                slotsPerShip = 5,
                requiredFreighterPercent = 60,
                requiredFighterPercent = 70,
                freighterPayment = new PaymentRange { min = 150, max = 170 },
                fighterPayment = new PaymentRange { min = 200, max = 220 },
                junkyard = new JunkyardBalance { price = 20, usableConditionMin = 70, usableConditionMax = 100 },
                days = days
            };
        }

        private static Run Make(int credits, int upkeep, int price, int ships, int seed)
        {
            return RunFactory.Create(MakeBalance(credits, upkeep, price, ships), ContentData.Empty(), seed);
        }

        private static void RepairAndRelease(Run run, Ship ship)
        {
            PartType[] types = PartTypes.All;
            for (int i = 0; i < types.Length; i++)
            {
                Part p = ship.GetPart(types[i]);
                if (p == null || !p.Passes(ship.RequiredLevel)) run.Buy(ship, types[i]);
            }
            run.Release(ship);
        }

        private static void ReleaseAll(Run run)
        {
            for (int guard = 0; guard < 100; guard++)
            {
                Ship[] ships = run.Bays.Ships;
                if (ships.Length == 0) return;
                run.Release(ships[0]);
            }
        }

        [Test]
        public void UpkeepIsChargedAtDayStart()
        {
            Run run = Make(1000, 120, 250, 2, 1);
            int dayStarts = 0;
            int creditsAtStart = 0;
            run.DayStarted += (d, n) =>
            {
                dayStarts++;
                creditsAtStart = run.Credits;
            };

            Assert.AreEqual(1000, run.Credits);
            Assert.IsTrue(run.StartDay().Success);

            Assert.AreEqual(880, run.Credits);
            Assert.AreEqual(1, dayStarts);
            Assert.AreEqual(880, creditsAtStart);
            Assert.AreEqual(2, run.Bays.Ships.Length);
        }

        [Test]
        public void CreditsAddUpWithBuyBinAndRelease()
        {
            Run run = Make(1000, 120, 50, 1, 2);
            DaySummary summary = null;
            run.DayEnded += s => summary = s;

            run.StartDay();
            Ship ship = run.Bays.GetShip(0);
            int payment = ship.Payment;
            int weak = ship.BelowLevelCount;
            Assert.GreaterOrEqual(weak, 1);

            RepairAndRelease(run, ship);
            Assert.AreEqual(1000 - 120 - 50 * weak + payment, run.Credits);

            Assert.IsNotNull(summary);
            Assert.AreEqual(1000, summary.StartCredits);
            Assert.AreEqual(payment, summary.Earned);
            Assert.AreEqual(50 * weak, summary.Spent);
            Assert.AreEqual(120, summary.Upkeep);
            Assert.AreEqual(run.Credits, summary.Balance);
            Assert.AreEqual(summary.StartCredits + summary.Earned - summary.Spent - summary.Upkeep, summary.Balance);
        }

        [Test]
        public void BinningPaysScrapAndCountsAsEarned()
        {
            Run run = Make(1000, 120, 50, 1, 3);
            DaySummary summary = null;
            run.DayEnded += s => summary = s;

            run.StartDay();
            Ship ship = run.Bays.GetShip(0);
            int payment = ship.Payment;
            RepairAndRelease(run, ship);
            Assert.IsNotNull(summary);
            Assert.AreEqual(payment, summary.Earned);

            Run second = Make(1000, 120, 50, 1, 3);
            DaySummary s2 = null;
            second.DayEnded += s => s2 = s;
            second.StartDay();
            Ship ship2 = second.Bays.GetShip(0);
            int weak = ship2.BelowLevelCount;
            PartType[] types = PartTypes.All;
            for (int i = 0; i < types.Length; i++)
            {
                Part p = ship2.GetPart(types[i]);
                if (p == null || !p.Passes(ship2.RequiredLevel)) second.Buy(ship2, types[i]);
            }
            int before = second.Credits;
            BinResult bin = second.Bin(PartSlot.InInventory(0));
            Assert.IsTrue(bin.Success);
            Assert.AreEqual(before + 10, second.Credits);
            second.Release(ship2);
            Assert.AreEqual(payment + 10, s2.Earned);
            Assert.AreEqual(50 * weak, s2.Spent);
            Assert.AreEqual(s2.StartCredits + s2.Earned - s2.Spent - s2.Upkeep, s2.Balance);
        }

        [Test]
        public void CreditsMayGoBelowZeroDuringTheDay()
        {
            Run run = Make(50, 120, 250, 2, 4);
            run.StartDay();
            Assert.AreEqual(-70, run.Credits);
            Assert.IsFalse(run.IsOver);
            Assert.AreEqual(RunPhase.InDay, run.Phase);
        }

        [Test]
        public void DayEndsWhenLastShipOfTheFixedCountIsReleased()
        {
            Run run = Make(100000, 120, 250, 5, 5);
            int ended = 0;
            run.DayEnded += s => ended++;

            run.StartDay();
            Assert.AreEqual(3, run.Bays.Ships.Length);
            Assert.AreEqual(5, run.ShipsLeftInDay);

            for (int i = 0; i < 4; i++)
            {
                run.Release(run.Bays.Ships[0]);
                Assert.AreEqual(0, ended);
            }
            Assert.AreEqual(1, run.Bays.Ships.Length);
            run.Release(run.Bays.Ships[0]);

            Assert.AreEqual(1, ended);
            Assert.AreEqual(0, run.Bays.Ships.Length);
            Assert.AreEqual(RunPhase.DayEnded, run.Phase);
        }

        [Test]
        public void BankruptcyOnlyAtDayEnd()
        {
            Run run = Make(0, 120, 250, 2, 6);
            int bankrupt = 0;
            int ended = 0;
            int runEnded = 0;
            run.Bankrupt += s => bankrupt++;
            run.DayEnded += s => ended++;
            run.RunEnded += o => runEnded++;

            run.StartDay();
            Assert.AreEqual(-120, run.Credits);
            Assert.AreEqual(0, bankrupt);
            Assert.IsFalse(run.IsOver);

            run.Release(run.Bays.Ships[0]);
            Assert.AreEqual(0, bankrupt);
            Assert.IsFalse(run.IsOver);

            run.Release(run.Bays.Ships[0]);
            Assert.AreEqual(1, ended);
            Assert.AreEqual(1, bankrupt);
            Assert.AreEqual(1, runEnded);
            Assert.IsTrue(run.IsOver);
            Assert.AreEqual(RunOutcome.Bankrupt, run.Outcome);
            Assert.AreEqual(1, run.Day);

            RunResult again = run.Continue();
            Assert.IsFalse(again.Success);
            Assert.AreEqual(RunRefusal.RunOver, again.Reason);
            Assert.AreEqual(RunRefusal.RunOver, run.StartDay().Reason);
        }

        [Test]
        public void RunEndsAfterDaySeven()
        {
            Run run = Make(10000, 120, 50, 1, 7);
            int runEnded = 0;
            int daysEnded = 0;
            run.RunEnded += o => runEnded++;
            run.DayEnded += s => daysEnded++;

            for (int d = 1; d <= 7; d++)
            {
                Assert.IsFalse(run.IsOver);
                Assert.AreEqual(d, run.Day);
                Assert.IsTrue(run.StartDay().Success);
                RepairAndRelease(run, run.Bays.Ships[0]);
                if (d < 7)
                {
                    Assert.AreEqual(RunPhase.DayEnded, run.Phase);
                    Assert.IsTrue(run.Continue().Success);
                }
            }

            Assert.IsTrue(run.IsOver);
            Assert.AreEqual(RunOutcome.Won, run.Outcome);
            Assert.AreEqual(7, daysEnded);
            Assert.AreEqual(1, runEnded);
            Assert.AreEqual(RunRefusal.RunOver, run.StartDay().Reason);
            Assert.AreEqual(RunRefusal.RunOver, run.Continue().Reason);
        }

        [Test]
        public void CommandsAreRefusedOutOfOrder()
        {
            Run run = Make(1000, 120, 250, 1, 8);
            Assert.AreEqual(RunRefusal.NoDayEnded, run.Continue().Reason);
            run.StartDay();
            Assert.AreEqual(RunRefusal.DayInProgress, run.StartDay().Reason);
            run.Release(run.Bays.Ships[0]);
            Assert.AreEqual(RunRefusal.SummaryPending, run.StartDay().Reason);
        }

        private static string Fingerprint(int seed)
        {
            Run run = Make(1000000, 120, 250, 3, seed);
            StringBuilder sb = new StringBuilder();
            run.Bays.ShipDocked += (ship, bay) =>
            {
                sb.Append(ship.Type).Append(':').Append(ship.Payment);
                Part[] parts = ship.Parts;
                for (int i = 0; i < parts.Length; i++) sb.Append(',').Append(parts[i].Condition);
                sb.Append(';');
            };
            for (int guard = 0; guard < 20 && !run.IsOver; guard++)
            {
                run.StartDay();
                ReleaseAll(run);
                if (!run.IsOver) run.Continue();
            }
            sb.Append("credits=").Append(run.Credits);
            return sb.ToString();
        }

        [Test]
        public void SameSeedGivesTheSameRun()
        {
            string a = Fingerprint(42);
            string b = Fingerprint(42);
            Assert.AreEqual(a, b);
            Assert.IsTrue(a.Length > 20);
        }
    }
}
