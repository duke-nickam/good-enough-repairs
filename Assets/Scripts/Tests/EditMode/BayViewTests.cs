using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GoodEnoughRepairs.Tests
{
    /// <summary>
    /// The commands behind the three ship views: each bay's ship can have parts
    /// moved between ships, can buy and can be released, all through the Run
    /// commands. The views only call these, so these tests cover what each of the
    /// three views does. The scene itself is not needed.
    /// </summary>
    public class BayViewTests
    {
        private BalanceData balance;
        private DockingBays bays;
        private Inventory inventory;
        private PartMoves moves;
        private BuyRelease buyRelease;

        [SetUp]
        public void SetUp()
        {
            string path = Path.Combine(Application.dataPath, "GameData/balance.json");
            string error;
            balance = BalanceLoader.Parse(File.ReadAllText(path), out error);
            Assert.IsNotNull(balance, "balance.json must load: " + error);

            bays = new DockingBays();
            inventory = new Inventory();
            moves = new PartMoves(inventory, balance);
            buyRelease = new BuyRelease(bays, inventory, balance);

            System.Random rng = new System.Random(5);
            for (int i = 0; i < bays.BayCount; i++)
            {
                bays.TryDock(ShipFactory.Create(balance, balance.days[0], rng));
            }
        }

        // The inventory slot that holds this exact part, or -1.
        private int SlotOf(Part part)
        {
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                if (ReferenceEquals(inventory.GetPart(i), part)) return i;
            }
            return -1;
        }

        [Test]
        public void EachBayHoldsItsOwnShip()
        {
            Assert.IsTrue(bays.IsFull);
            Ship a = bays.GetShip(0);
            Ship b = bays.GetShip(1);
            Ship c = bays.GetShip(2);
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsNotNull(c);
            Assert.AreNotSame(a, b);
            Assert.AreNotSame(b, c);
            Assert.AreEqual(0, bays.GetBay(a));
            Assert.AreEqual(1, bays.GetBay(b));
            Assert.AreEqual(2, bays.GetBay(c));
        }

        [Test]
        public void EmptyBayHasNoShip()
        {
            Ship middle = bays.GetShip(1);
            bays.Remove(1);
            Assert.IsNull(bays.GetShip(1));
            Assert.IsNotNull(bays.GetShip(0));
            Assert.IsNotNull(bays.GetShip(2));
            Assert.AreEqual(-1, bays.GetBay(middle));
        }

        [Test]
        public void DragBetweenShipsInBays2And3SwapsTheParts()
        {
            Ship s2 = bays.GetShip(1);
            Ship s3 = bays.GetShip(2);
            Part p2 = s2.GetPart(PartType.Engine);
            Part p3 = s3.GetPart(PartType.Engine);
            Assert.IsNotNull(p2);
            Assert.IsNotNull(p3);

            MoveResult r = moves.Move(PartSlot.OnShip(s2, PartType.Engine), PartSlot.OnShip(s3, PartType.Engine));

            Assert.IsTrue(r.Success);
            Assert.AreSame(p3, s2.GetPart(PartType.Engine));
            Assert.AreSame(p2, s3.GetPart(PartType.Engine));
        }

        [Test]
        public void DragToWrongSlotTypeSnapsBack()
        {
            Ship s1 = bays.GetShip(0);
            Ship s3 = bays.GetShip(2);
            Part engine = s1.GetPart(PartType.Engine);
            Part hull = s3.GetPart(PartType.HullPlate);

            MoveResult r = moves.Move(PartSlot.OnShip(s1, PartType.Engine), PartSlot.OnShip(s3, PartType.HullPlate));

            Assert.IsFalse(r.Success);
            Assert.AreSame(engine, s1.GetPart(PartType.Engine));
            Assert.AreSame(hull, s3.GetPart(PartType.HullPlate));
        }

        [Test]
        public void DragFromEachShipToInventoryAndBackWorks()
        {
            for (int bay = 0; bay < bays.BayCount; bay++)
            {
                Ship ship = bays.GetShip(bay);
                Part thruster = ship.GetPart(PartType.Thruster);
                Assert.IsNotNull(thruster);

                MoveResult toInv = moves.Move(PartSlot.OnShip(ship, PartType.Thruster), PartSlot.InInventoryAny());
                Assert.IsTrue(toInv.Success, "bay " + (bay + 1) + " to inventory");
                Assert.IsNull(ship.GetPart(PartType.Thruster));

                int slot = SlotOf(thruster);
                Assert.GreaterOrEqual(slot, 0, "part is in the inventory");
                MoveResult back = moves.Move(PartSlot.InInventory(slot), PartSlot.OnShip(ship, PartType.Thruster));
                Assert.IsTrue(back.Success, "bay " + (bay + 1) + " back to ship");
                Assert.AreSame(thruster, ship.GetPart(PartType.Thruster));
            }
        }

        [Test]
        public void DragToFullInventorySnapsBack()
        {
            int slot;
            while (inventory.FirstFreeSlot >= 0) inventory.TryAdd(new Part(PartType.Engine, 50), out slot);

            Ship s3 = bays.GetShip(2);
            Part engine = s3.GetPart(PartType.Engine);

            MoveResult r = moves.Move(PartSlot.OnShip(s3, PartType.Engine), PartSlot.InInventoryAny());

            Assert.IsFalse(r.Success);
            Assert.AreSame(engine, s3.GetPart(PartType.Engine));
        }

        [Test]
        public void BuyWorksFromEveryBay()
        {
            for (int bay = 0; bay < bays.BayCount; bay++)
            {
                Ship ship = bays.GetShip(bay);
                PartType found = PartType.Engine;
                bool any = false;
                PartType[] types = PartTypes.All;
                for (int i = 0; i < types.Length && !any; i++)
                {
                    if (buyRelease.PreviewBuy(ship, types[i]).CanBuy)
                    {
                        found = types[i];
                        any = true;
                    }
                }
                Assert.IsTrue(any, "bay " + (bay + 1) + " should have a slot below the level");

                BuyResult r = buyRelease.Buy(ship, found);
                Assert.IsTrue(r.Success);
                Assert.AreEqual(Part.MaxCondition, ship.GetPart(found).Condition);
                Assert.IsFalse(buyRelease.PreviewBuy(ship, found).CanBuy);
            }
        }

        [Test]
        public void ReleaseWorksFromEveryBayAndFreesIt()
        {
            for (int bay = 0; bay < bays.BayCount; bay++)
            {
                Ship ship = bays.GetShip(bay);
                ReleasePreview preview = buyRelease.PreviewRelease(ship);
                ReleaseResult r = buyRelease.Release(ship);

                Assert.IsTrue(r.Success);
                Assert.AreEqual(bay, r.Bay);
                Assert.AreEqual(preview.NetResult, r.NetResult);
                Assert.AreEqual(ship.Payment - r.PenaltyFills * r.PenaltyPrice, r.NetResult);
                Assert.IsNull(bays.GetShip(bay));
            }
            Assert.AreEqual(bays.BayCount, bays.FreeBayCount);
        }

        [Test]
        public void ReleaseFillsEveryWeakSlotSoTheShipLeavesRepaired()
        {
            Ship ship = bays.GetShip(0);
            int weak = ship.BelowLevelCount;
            Assert.Greater(weak, 0, "the sample ship starts with weak parts");

            ReleaseResult r = buyRelease.Release(ship);

            Assert.IsTrue(r.Success);
            Assert.AreEqual(weak, r.PenaltyFills);
            Assert.AreEqual(0, ship.BelowLevelCount);
        }

        [Test]
        public void ReleasingAShipTwiceIsRefused()
        {
            Ship ship = bays.GetShip(1);
            Assert.IsTrue(buyRelease.Release(ship).Success);

            ReleaseResult again = buyRelease.Release(ship);

            Assert.IsFalse(again.Success);
            Assert.AreEqual(ReleaseRefusal.NotDocked, again.Reason);
        }
    }
}
