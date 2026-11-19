using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace GoodEnoughRepairs.Tests
{
    public class InventoryTests
    {
        private const int TestScrap = 7;

        private Inventory inventory;
        private PartMoves moves;
        private List<MoveResult> movedEvents;
        private List<BinResult> binnedEvents;

        private static PartType Engine { get { return PartTypes.All[0]; } }
        private static PartType Hull { get { return PartTypes.All[1]; } }

        [SetUp]
        public void SetUp()
        {
            BalanceData balance = new BalanceData();
            balance.scrapPrice = TestScrap;
            inventory = new Inventory();
            moves = new PartMoves(inventory, balance);
            movedEvents = new List<MoveResult>();
            binnedEvents = new List<BinResult>();
            moves.PartMoved += r => movedEvents.Add(r);
            moves.PartBinned += r => binnedEvents.Add(r);
        }

        private static Ship MakeShip()
        {
            return (Ship)System.Activator.CreateInstance(
                typeof(Ship),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new object[] { default(ShipType), 100, 60 },
                null);
        }

        private static void Install(Ship ship, Part part)
        {
            MethodInfo m = typeof(Ship).GetMethod("SetPart", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m.Invoke(ship, new object[] { part.Type, part });
        }

        private void FillInventory()
        {
            int slot;
            for (int i = 0; i < GameRules.InventorySlots; i++)
                Assert.IsTrue(inventory.TryAdd(new Part(Hull, 50), out slot));
        }

        [Test]
        public void InventoryHasSlotsFromTheNamedConstant()
        {
            Assert.AreEqual(GameRules.InventorySlots, inventory.SlotCount);
            Assert.AreEqual(GameRules.InventorySlots, inventory.Parts.Length);
        }

        [Test]
        public void SwapShipToShip()
        {
            Ship a = MakeShip();
            Ship b = MakeShip();
            Part pa = new Part(Engine, 35);
            Part pb = new Part(Engine, 65);
            Install(a, pa);
            Install(b, pb);

            MoveResult r = moves.Move(PartSlot.OnShip(b, Engine), PartSlot.OnShip(a, Engine));

            Assert.IsTrue(r.Success);
            Assert.IsTrue(r.IsSwap);
            Assert.AreSame(pb, a.GetPart(Engine));
            Assert.AreSame(pa, b.GetPart(Engine));
        }

        [Test]
        public void MoveShipToEmptyShipSlot()
        {
            Ship a = MakeShip();
            Ship b = MakeShip();
            Part pa = new Part(Engine, 80);
            Install(a, pa);

            MoveResult r = moves.Move(PartSlot.OnShip(a, Engine), PartSlot.OnShip(b, Engine));

            Assert.IsTrue(r.Success);
            Assert.IsFalse(r.IsSwap);
            Assert.IsNull(a.GetPart(Engine));
            Assert.AreSame(pa, b.GetPart(Engine));
        }

        [Test]
        public void SwapShipToInventory()
        {
            Ship a = MakeShip();
            Part onShip = new Part(Engine, 35);
            Part stored = new Part(Engine, 90);
            Install(a, onShip);
            int slot;
            inventory.TryAdd(stored, out slot);

            MoveResult r = moves.Move(PartSlot.OnShip(a, Engine), PartSlot.InInventory(slot));

            Assert.IsTrue(r.Success);
            Assert.AreSame(stored, a.GetPart(Engine));
            Assert.AreSame(onShip, inventory.GetPart(slot));
        }

        [Test]
        public void SwapInventoryToShip()
        {
            Ship a = MakeShip();
            Part onShip = new Part(Engine, 35);
            Part stored = new Part(Engine, 90);
            Install(a, onShip);
            int slot;
            inventory.TryAdd(stored, out slot);

            MoveResult r = moves.Move(PartSlot.InInventory(slot), PartSlot.OnShip(a, Engine));

            Assert.IsTrue(r.Success);
            Assert.AreSame(stored, a.GetPart(Engine));
            Assert.AreSame(onShip, inventory.GetPart(slot));
        }

        [Test]
        public void ShipToInventoryAnyUsesFirstFreeSlotAndLeavesShipSlotEmpty()
        {
            Ship a = MakeShip();
            Part p = new Part(Engine, 35);
            Install(a, p);
            int s;
            inventory.TryAdd(new Part(Hull, 50), out s);

            MoveResult r = moves.Move(PartSlot.OnShip(a, Engine), PartSlot.InInventoryAny());

            Assert.IsTrue(r.Success);
            Assert.IsNull(a.GetPart(Engine));
            Assert.AreSame(p, inventory.GetPart(1));
            Assert.AreEqual(1, r.To.InventoryIndex);
        }

        [Test]
        public void ShipToChosenEmptyInventorySlot()
        {
            Ship a = MakeShip();
            Part p = new Part(Engine, 35);
            Install(a, p);

            MoveResult r = moves.Move(PartSlot.OnShip(a, Engine), PartSlot.InInventory(4));

            Assert.IsTrue(r.Success);
            Assert.AreSame(p, inventory.GetPart(4));
            Assert.IsNull(inventory.GetPart(0));
        }

        [Test]
        public void InventoryToShipLeavesGapAndPartsDoNotShift()
        {
            Ship a = MakeShip();
            int s0, s1;
            Part first = new Part(Hull, 50);
            Part second = new Part(Engine, 70);
            inventory.TryAdd(first, out s0);
            inventory.TryAdd(second, out s1);

            moves.Move(PartSlot.InInventory(s0), PartSlot.OnShip(a, Hull));

            Assert.IsNull(inventory.GetPart(0));
            Assert.AreSame(second, inventory.GetPart(1));
        }

        [Test]
        public void FullInventoryRefusesPlainMoveAndPartSnapsBack()
        {
            Ship a = MakeShip();
            Part p = new Part(Engine, 35);
            Install(a, p);
            FillInventory();

            MoveResult r = moves.Move(PartSlot.OnShip(a, Engine), PartSlot.InInventoryAny());

            Assert.IsFalse(r.Success);
            Assert.AreEqual(MoveRefusal.InventoryFull, r.Reason);
            Assert.AreSame(p, a.GetPart(Engine));
            Assert.AreEqual(GameRules.InventorySlots, inventory.Count);
            Assert.AreEqual(0, movedEvents.Count);
        }

        [Test]
        public void SwapIntoFullInventoryIsAllowed()
        {
            Ship a = MakeShip();
            Part p = new Part(Hull, 35);
            Install(a, p);
            FillInventory();
            Part stored = inventory.GetPart(3);

            MoveResult r = moves.Move(PartSlot.OnShip(a, Hull), PartSlot.InInventory(3));

            Assert.IsTrue(r.Success);
            Assert.AreSame(stored, a.GetPart(Hull));
            Assert.AreSame(p, inventory.GetPart(3));
            Assert.AreEqual(GameRules.InventorySlots, inventory.Count);
        }

        [Test]
        public void WrongTypeIsRefusedAndNothingChanges()
        {
            Ship a = MakeShip();
            Part engine = new Part(Engine, 80);
            Install(a, engine);

            MoveResult r = moves.Move(PartSlot.OnShip(a, Engine), PartSlot.OnShip(a, Hull));

            Assert.IsFalse(r.Success);
            Assert.AreEqual(MoveRefusal.WrongSlotType, r.Reason);
            Assert.AreSame(engine, a.GetPart(Engine));
            Assert.IsNull(a.GetPart(Hull));
            Assert.AreEqual(0, movedEvents.Count);
        }

        [Test]
        public void SwapIsRefusedWhenOnePartDoesNotFitTheOtherSlot()
        {
            Ship a = MakeShip();
            Part engine = new Part(Engine, 80);
            Part hull = new Part(Hull, 80);
            Install(a, engine);
            int slot;
            inventory.TryAdd(hull, out slot);

            MoveResult r = moves.Move(PartSlot.InInventory(slot), PartSlot.OnShip(a, Engine));

            Assert.IsFalse(r.Success);
            Assert.AreSame(engine, a.GetPart(Engine));
            Assert.AreSame(hull, inventory.GetPart(slot));
        }

        [Test]
        public void SameSlotAndEmptySourceAndNullAreRefused()
        {
            Ship a = MakeShip();
            Install(a, new Part(Engine, 80));

            Assert.AreEqual(MoveRefusal.SameSlot,
                moves.Move(PartSlot.OnShip(a, Engine), PartSlot.OnShip(a, Engine)).Reason);
            Assert.AreEqual(MoveRefusal.SourceEmpty,
                moves.Move(PartSlot.InInventory(0), PartSlot.OnShip(a, Engine)).Reason);
            Assert.AreEqual(MoveRefusal.InvalidSlot,
                moves.Move(null, PartSlot.InInventory(0)).Reason);
            Assert.AreEqual(MoveRefusal.InvalidSlot,
                moves.Move(PartSlot.OnShip(a, Engine), PartSlot.InInventory(GameRules.InventorySlots)).Reason);
            Assert.AreEqual(0, movedEvents.Count);
        }

        [Test]
        public void BinFromInventoryRemovesPartAndReportsScrapOnce()
        {
            int slot;
            Part p = new Part(Hull, 20);
            inventory.TryAdd(p, out slot);

            BinResult r = moves.Bin(PartSlot.InInventory(slot));

            Assert.IsTrue(r.Success);
            Assert.AreEqual(TestScrap, r.ScrapAmount);
            Assert.AreSame(p, r.Part);
            Assert.IsNull(inventory.GetPart(slot));
            Assert.AreEqual(1, binnedEvents.Count);
            Assert.AreEqual(TestScrap, binnedEvents[0].ScrapAmount);

            BinResult again = moves.Bin(PartSlot.InInventory(slot));
            Assert.IsFalse(again.Success);
            Assert.AreEqual(0, again.ScrapAmount);
            Assert.AreEqual(1, binnedEvents.Count);
        }

        [Test]
        public void BinFromShipLeavesSlotEmpty()
        {
            Ship a = MakeShip();
            Install(a, new Part(Engine, 35));

            BinResult r = moves.Bin(PartSlot.OnShip(a, Engine));

            Assert.IsTrue(r.Success);
            Assert.IsNull(a.GetPart(Engine));
            Assert.AreEqual(TestScrap, r.ScrapAmount);
        }

        [Test]
        public void BinRefusalsDoNotThrowOrFireEvents()
        {
            Assert.AreEqual(MoveRefusal.InvalidSlot, moves.Bin(null).Reason);
            Assert.AreEqual(MoveRefusal.InvalidSlot, moves.Bin(PartSlot.InInventoryAny()).Reason);
            Assert.AreEqual(MoveRefusal.SourceEmpty, moves.Bin(PartSlot.InInventory(0)).Reason);
            Assert.AreEqual(0, binnedEvents.Count);
        }

        [Test]
        public void MovedEventFiresAfterStateChangeOnceForASwap()
        {
            Ship a = MakeShip();
            Part onShip = new Part(Engine, 35);
            Part stored = new Part(Engine, 90);
            Install(a, onShip);
            int slot;
            inventory.TryAdd(stored, out slot);

            Part seenOnShip = null;
            Part seenInInventory = null;
            moves.PartMoved += r =>
            {
                seenOnShip = a.GetPart(Engine);
                seenInInventory = inventory.GetPart(slot);
            };

            moves.Move(PartSlot.OnShip(a, Engine), PartSlot.InInventory(slot));

            Assert.AreEqual(1, movedEvents.Count);
            Assert.AreSame(stored, seenOnShip);
            Assert.AreSame(onShip, seenInInventory);
            Assert.AreSame(onShip, movedEvents[0].Part);
            Assert.AreSame(stored, movedEvents[0].SwappedPart);
        }

        [Test]
        public void BinnedEventFiresAfterStateChange()
        {
            int slot;
            inventory.TryAdd(new Part(Hull, 20), out slot);
            bool emptyWhenFired = false;
            moves.PartBinned += r => emptyWhenFired = inventory.GetPart(slot) == null;

            moves.Bin(PartSlot.InInventory(slot));

            Assert.IsTrue(emptyWhenFired);
            Assert.AreEqual(1, binnedEvents.Count);
        }
    }
}
