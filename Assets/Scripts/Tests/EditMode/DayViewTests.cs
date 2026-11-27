using NUnit.Framework;

namespace GoodEnoughRepairs.Tests
{
    // Tests of what the credits counter and the day summary panel show. Their
    // logic lives in CreditsDisplay and SummaryPanelState (Logic); the views
    // only display it.
    public class DayViewTests
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
        public void CreditsShowRunCreditsAndFollowChanges()
        {
            CreditsDisplay view = new CreditsDisplay();
            Run run = Make(1000, 120, 250, 1, 1);

            view.Bind(run);
            Assert.AreEqual(1000, view.Shown);

            run.StartDay();
            Assert.AreEqual(880, view.Shown);

            run.Release(run.Bays.Ships[0]);
            Assert.AreEqual(run.Credits, view.Shown);
        }

        [Test]
        public void CreditsShowNegativeCredits()
        {
            CreditsDisplay view = new CreditsDisplay();
            Run run = Make(50, 120, 250, 2, 2);
            view.Bind(run);
            run.StartDay();
            Assert.AreEqual(-70, view.Shown);
        }

        [Test]
        public void CreditsFollowTheNewRunAfterBindingAgain()
        {
            CreditsDisplay view = new CreditsDisplay();
            Run first = Make(1000, 120, 250, 1, 3);
            view.Bind(first);
            Run second = Make(500, 120, 250, 1, 4);
            view.Bind(second);
            Assert.AreEqual(500, view.Shown);
            first.StartDay();
            Assert.AreEqual(500, view.Shown);
        }

        [Test]
        public void SummaryIsClosedUntilADayEnds()
        {
            SummaryPanelState view = new SummaryPanelState();
            Run run = Make(100000, 120, 50, 1, 5);
            view.Bind(run, null, null);
            run.StartDay();
            Assert.IsFalse(view.IsOpen);
        }

        [Test]
        public void SummaryShowsTheDayNumbersWithContinue()
        {
            SummaryPanelState view = new SummaryPanelState();
            Run run = Make(100000, 120, 50, 1, 6);
            view.Bind(run, null, null);
            run.StartDay();
            run.Release(run.Bays.Ships[0]);

            Assert.IsTrue(view.IsOpen);
            Assert.IsTrue(view.ShowsContinue);
            Assert.IsFalse(view.ShowsPlayAgain);
            Assert.IsFalse(view.IsRunOver);
            Assert.AreEqual("summary_title", view.TitleKey);
            Assert.AreEqual(run.LastSummary.Earned, view.Shown.Earned);
            Assert.AreEqual(run.LastSummary.Spent, view.Shown.Spent);
            Assert.AreEqual(120, view.Shown.Upkeep);
            Assert.AreEqual(run.Credits, view.Shown.Balance);
        }

        [Test]
        public void ContinueClosesThePanelAndMovesTheRunOn()
        {
            SummaryPanelState view = new SummaryPanelState();
            Run run = Make(100000, 120, 50, 1, 7);
            int continued = 0;
            view.Bind(run, () => continued++, null);
            run.StartDay();
            run.Release(run.Bays.Ships[0]);

            Assert.IsTrue(view.PressContinue());

            Assert.IsFalse(view.IsOpen);
            Assert.AreEqual(1, continued);
            Assert.AreEqual(RunPhase.BeforeDay, run.Phase);
            Assert.AreEqual(2, run.Day);
        }

        [Test]
        public void BankruptcySaysRunOverAndOffersPlayAgain()
        {
            SummaryPanelState view = new SummaryPanelState();
            Run run = Make(0, 120, 250, 1, 8);
            int again = 0;
            view.Bind(run, null, () => again++);
            run.StartDay();
            run.Release(run.Bays.Ships[0]);

            Assert.IsTrue(run.IsOver);
            Assert.IsTrue(view.IsOpen);
            Assert.IsTrue(view.IsRunOver);
            Assert.IsFalse(view.ShowsContinue);
            Assert.IsTrue(view.ShowsPlayAgain);
            Assert.AreEqual("run_end_title_bankrupt", view.TitleKey);

            Assert.IsFalse(view.PressContinue());
            Assert.IsTrue(view.IsOpen);

            Assert.IsTrue(view.PressPlayAgain());
            Assert.IsFalse(view.IsOpen);
            Assert.AreEqual(1, again);
        }

        [Test]
        public void DaySevenEndsTheRunWithTheWinTitle()
        {
            SummaryPanelState view = new SummaryPanelState();
            Run run = Make(100000, 120, 50, 1, 9);
            view.Bind(run, null, null);

            for (int d = 1; d <= 7; d++)
            {
                run.StartDay();
                ReleaseAll(run);
                if (d < 7)
                {
                    Assert.IsTrue(view.ShowsContinue);
                    view.PressContinue();
                }
            }

            Assert.IsTrue(run.IsOver);
            Assert.AreEqual(RunOutcome.Won, run.Outcome);
            Assert.IsTrue(view.IsRunOver);
            Assert.IsTrue(view.ShowsPlayAgain);
            Assert.IsFalse(view.ShowsContinue);
            Assert.AreEqual("run_end_title_win", view.TitleKey);
        }

        [Test]
        public void PlayAgainIsRefusedWhileTheRunGoesOn()
        {
            SummaryPanelState view = new SummaryPanelState();
            Run run = Make(100000, 120, 50, 1, 10);
            int again = 0;
            view.Bind(run, null, () => again++);
            run.StartDay();
            run.Release(run.Bays.Ships[0]);

            Assert.IsFalse(view.PressPlayAgain());
            Assert.AreEqual(0, again);
            Assert.IsTrue(view.IsOpen);
        }

        [Test]
        public void RunCommandObjectsFeedTheRunsCredits()
        {
            Run run = Make(1000, 120, 250, 1, 11);
            run.StartDay();
            int before = run.Credits;
            Ship ship = run.Bays.Ships[0];
            ReleaseResult result = run.Commands.Release(ship);
            Assert.AreEqual(before + result.NetResult, run.Credits);
        }
    }
}
