using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace GoodEnoughRepairs
{
    public class BaysTests
    {
        private static BalanceData LoadBalance()
        {
            string path = Path.Combine(Path.Combine(UnityEngine.Application.dataPath, "StreamingAssets"), BalanceLoader.FileName);
            string json = File.ReadAllText(path);
            string error;
            BalanceData data = BalanceLoader.Parse(json, out error);
            Assert.IsNotNull(data, error);
            return data;
        }

        private static Ship NewShip(BalanceData b, Random rng)
        {
            return ShipFactory.Create(b, b.days[0], rng);
        }

        [Test]
        public void Station_HasExactlyThreeBays()
        {
            DockingBays bays = new DockingBays();
            Assert.AreEqual(3, bays.BayCount);
            Assert.AreEqual(3, bays.FreeBayCount);
            Assert.IsFalse(bays.IsFull);
        }

        [Test]
        public void Docking_UsesLowestFreeBay_AndFullStationIsRefused()
        {
            BalanceData b = LoadBalance();
            Random rng = new Random(1);
            DockingBays bays = new DockingBays();
            for (int i = 0; i < 3; i++)
            {
                BayResult r = bays.TryDock(NewShip(b, rng));
                Assert.IsTrue(r.Success);
                Assert.AreEqual(i, r.Bay);
            }
            Assert.IsTrue(bays.IsFull);
            BayResult refused = bays.TryDock(NewShip(b, rng));
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(BayRefusal.StationFull, refused.Reason);
            Assert.AreEqual(3, bays.Ships.Length);
        }

        [Test]
        public void Docking_NullAndDuplicate_AreRefusedNotThrown()
        {
            BalanceData b = LoadBalance();
            DockingBays bays = new DockingBays();
            Assert.AreEqual(BayRefusal.NullShip, bays.TryDock(null).Reason);
            Ship s = NewShip(b, new Random(2));
            Assert.IsTrue(bays.TryDock(s).Success);
            Assert.AreEqual(BayRefusal.AlreadyDocked, bays.TryDock(s).Reason);
            Assert.AreEqual(1, bays.Ships.Length);
        }

        [Test]
        public void Remove_FreesBay_AndRefusalsHaveReasons()
        {
            BalanceData b = LoadBalance();
            Random rng = new Random(3);
            DockingBays bays = new DockingBays();
            Ship a = NewShip(b, rng);
            Ship c = NewShip(b, rng);
            bays.TryDock(a);
            bays.TryDock(c);
            BayResult r = bays.Remove(a);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(0, r.Bay);
            Assert.IsNull(bays.GetShip(0));
            Assert.AreSame(c, bays.GetShip(1));
            Assert.AreEqual(BayRefusal.NotDocked, bays.Remove(a).Reason);
            Assert.AreEqual(BayRefusal.BayEmpty, bays.Remove(0).Reason);
            Assert.AreEqual(BayRefusal.InvalidBay, bays.Remove(5).Reason);
            Assert.AreEqual(BayRefusal.NullShip, bays.Remove((Ship)null).Reason);
            // The freed bay is used next.
            Assert.AreEqual(0, bays.TryDock(NewShip(b, rng)).Bay);
        }

        [Test]
        public void Events_FireAfterStateChange_WithShipAndBay()
        {
            BalanceData b = LoadBalance();
            DockingBays bays = new DockingBays();
            Ship s = NewShip(b, new Random(4));
            List<string> log = new List<string>();
            bays.ShipDocked += (ship, bay) =>
            {
                Assert.AreSame(s, ship);
                Assert.AreSame(s, bays.GetShip(bay));
                log.Add("docked" + bay);
            };
            bays.ShipLeft += (ship, bay) =>
            {
                Assert.AreSame(s, ship);
                Assert.IsNull(bays.GetShip(bay));
                log.Add("left" + bay);
            };
            bays.TryDock(s);
            bays.Remove(s);
            CollectionAssert.AreEqual(new[] { "docked0", "left0" }, log);
        }

        [Test]
        public void Refused_RequestsFireNoEvents()
        {
            BalanceData b = LoadBalance();
            DockingBays bays = new DockingBays();
            int count = 0;
            bays.ShipDocked += (s, i) => count++;
            bays.ShipLeft += (s, i) => count++;
            bays.TryDock(null);
            bays.Remove(0);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void EveryShip_HasFiveTypedParts_Payment_AndLevel()
        {
            BalanceData b = LoadBalance();
            Random rng = new Random(5);
            for (int d = 0; d < b.days.Length; d++)
            {
                for (int n = 0; n < 200; n++)
                {
                    Ship s = ShipFactory.Create(b, b.days[d], rng);
                    Part[] parts = s.Parts;
                    Assert.AreEqual(5, parts.Length);
                    PartType[] types = PartTypes.All;
                    for (int i = 0; i < types.Length; i++)
                    {
                        Assert.IsNotNull(parts[i]);
                        Assert.AreEqual(types[i], parts[i].Type);
                        Assert.AreSame(parts[i], s.GetPart(types[i]));
                    }
                    bool fighter = s.Type == ShipType.Fighter;
                    PaymentRange range = fighter ? b.fighterPayment : b.freighterPayment;
                    Assert.GreaterOrEqual(s.Payment, range.min);
                    Assert.LessOrEqual(s.Payment, range.max);
                    Assert.AreEqual(fighter ? b.requiredFighterPercent : b.requiredFreighterPercent, s.RequiredLevel);
                }
            }
        }

        [Test]
        public void EveryShip_HasWeakParts_WithinDayRange()
        {
            BalanceData b = LoadBalance();
            Random rng = new Random(6);
            for (int d = 0; d < b.days.Length; d++)
            {
                DayBalance day = b.days[d];
                for (int n = 0; n < 200; n++)
                {
                    Ship s = ShipFactory.Create(b, day, rng);
                    Assert.GreaterOrEqual(s.BelowLevelCount, 1);
                    Assert.GreaterOrEqual(s.BelowLevelCount, day.weakPartsMin);
                    Assert.LessOrEqual(s.BelowLevelCount, day.weakPartsMax);
                    Assert.IsFalse(s.IsRepaired);
                    Assert.IsFalse(s.WeakestPart.Passes(s.RequiredLevel));
                }
            }
        }

        [Test]
        public void BrokenWeakRange_StillGivesWeakPart()
        {
            BalanceData b = LoadBalance();
            DayBalance day = b.days[0];
            day.fighterWeakConditionMin = 90;
            day.fighterWeakConditionMax = 95;
            day.freighterWeakConditionMin = 90;
            day.freighterWeakConditionMax = 95;
            Random rng = new Random(7);
            for (int n = 0; n < 100; n++)
            {
                Ship s = ShipFactory.Create(b, day, rng);
                Assert.GreaterOrEqual(s.BelowLevelCount, 1);
            }
        }

        [Test]
        public void WeakestPart_IsTheLowestCondition()
        {
            BalanceData b = LoadBalance();
            Random rng = new Random(8);
            for (int n = 0; n < 100; n++)
            {
                Ship s = NewShip(b, rng);
                int min = 101;
                Part[] parts = s.Parts;
                for (int i = 0; i < parts.Length; i++) if (parts[i].Condition < min) min = parts[i].Condition;
                Assert.AreEqual(min, s.WeakestPart.Condition);
            }
        }

        [Test]
        public void PartsView_IsACopy()
        {
            BalanceData b = LoadBalance();
            Ship s = NewShip(b, new Random(9));
            Part[] copy = s.Parts;
            Part original = s.GetPart(PartType.Engine);
            copy[0] = null;
            Assert.AreSame(original, s.GetPart(PartType.Engine));
        }

        [Test]
        public void BothShipTypes_Appear()
        {
            BalanceData b = LoadBalance();
            Random rng = new Random(10);
            bool fighter = false, freighter = false;
            for (int n = 0; n < 100; n++)
            {
                Ship s = NewShip(b, rng);
                if (s.Type == ShipType.Fighter) fighter = true; else freighter = true;
            }
            Assert.IsTrue(fighter);
            Assert.IsTrue(freighter);
        }

        [Test]
        public void SameSeed_GivesSameShips()
        {
            BalanceData b = LoadBalance();
            Random r1 = new Random(123);
            Random r2 = new Random(123);
            for (int n = 0; n < 50; n++)
            {
                DayBalance day = b.days[n % b.days.Length];
                Ship a = ShipFactory.Create(b, day, r1);
                Ship c = ShipFactory.Create(b, day, r2);
                Assert.AreEqual(a.Type, c.Type);
                Assert.AreEqual(a.Payment, c.Payment);
                Assert.AreEqual(a.RequiredLevel, c.RequiredLevel);
                PartType[] types = PartTypes.All;
                for (int i = 0; i < types.Length; i++)
                {
                    Assert.AreEqual(a.GetPart(types[i]).Condition, c.GetPart(types[i]).Condition);
                }
            }
        }
    }
}
