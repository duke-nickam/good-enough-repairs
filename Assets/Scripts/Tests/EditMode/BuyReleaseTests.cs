using System.Reflection;
using NUnit.Framework;

namespace GoodEnoughRepairs.Tests
{
    public class BuyReleaseTests
    {
        private BalanceData balance;
        private DockingBays bays;
        private Inventory inventory;
        private BuyRelease sut;

        [SetUp]
        public void SetUp()
        {
            balance = new BalanceData();
            balance.newPartPrice = 250;
            balance.penaltyPercent = 20;
            balance.scrapPrice = 10;
            balance.requiredFreighterPercent = 60;
            balance.requiredFighterPercent = 70;
            bays = new DockingBays();
            inventory = new Inventory();
            sut = new BuyRelease(bays, inventory, balance);
        }

        private Ship MakeShip(ShipType type, int payment, int level, params int[] conditions)
        {
            Ship ship = (Ship)System.Activator.CreateInstance(typeof(Ship),
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { type, payment, level }, null);
            MethodInfo set = typeof(Ship).GetMethod("SetPart", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i] < 0) continue;
                set.Invoke(ship, new object[] { (PartType)i, new Part((PartType)i, conditions[i]) });
            }
            return ship;
        }

        private Ship Docked(ShipType type, int payment, int level, params int[] conditions)
        {
            Ship s = MakeShip(type, payment, level, conditions);
            Assert.IsTrue(bays.TryDock(s).Success);
            return s;
        }

        [Test]
        public void Buy_WeakPart_PutsNewPartAndOldGoesToInventory()
        {
            Ship s = Docked(ShipType.Freighter, 160, 60, 35, 80, 80, 80, 80);
            Part old = s.GetPart((PartType)0);
            BuyResult r = sut.Buy(s, (PartType)0);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(100, s.GetPart((PartType)0).Condition);
            Assert.AreEqual(250, r.Price);
            Assert.IsFalse(r.OldPartBinned);
            Assert.AreSame(old, inventory.GetPart(r.InventorySlot));
        }

        [Test]
        public void Buy_EmptySlot_SendsNothingToInventory()
        {
            Ship s = Docked(ShipType.Freighter, 160, 60, -1, 80, 80, 80, 80);
            BuyResult r = sut.Buy(s, (PartType)0);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(0, inventory.Count);
            Assert.IsNull(r.OldPart);
        }

        [Test]
        public void Buy_GoodEnoughPart_IsRefusedWithReason()
        {
            Ship s = Docked(ShipType.Freighter, 160, 60, 60, 80, 80, 80, 80);
            BuyResult r = sut.Buy(s, (PartType)0);
            Assert.IsFalse(r.Success);
            Assert.AreEqual(BuyRefusal.PartGoodEnough, r.Reason);
            Assert.AreEqual(60, s.GetPart((PartType)0).Condition);
        }

        [Test]
        public void Buy_RequiredLevelDependsOnShip()
        {
            Ship fighter = Docked(ShipType.Fighter, 210, 70, 65, 80, 80, 80, 80);
            Assert.IsTrue(sut.PreviewBuy(fighter, (PartType)0).CanBuy);
            Ship freighter = Docked(ShipType.Freighter, 160, 60, 65, 80, 80, 80, 80);
            Assert.IsFalse(sut.PreviewBuy(freighter, (PartType)0).CanBuy);
        }

        [Test]
        public void Buy_NullShipAndNotDocked_AreRefused()
        {
            Assert.AreEqual(BuyRefusal.NullShip, sut.Buy(null, (PartType)0).Reason);
            Ship loose = MakeShip(ShipType.Freighter, 160, 60, 10, 80, 80, 80, 80);
            Assert.AreEqual(BuyRefusal.NotDocked, sut.Buy(loose, (PartType)0).Reason);
            Assert.AreEqual(10, loose.GetPart((PartType)0).Condition);
        }

        [Test]
        public void Buy_FullInventory_BinsOldPartAndPreviewSaysSo()
        {
            for (int i = 0; i < GameRules.InventorySlots; i++)
            {
                int slot;
                inventory.TryAdd(new Part((PartType)0, 90), out slot);
            }
            Ship s = Docked(ShipType.Freighter, 160, 60, 35, 80, 80, 80, 80);
            BuyPreview p = sut.PreviewBuy(s, (PartType)0);
            Assert.IsTrue(p.OldPartBinned);
            Assert.AreEqual(10, p.ScrapAmount);
            BuyResult r = sut.Buy(s, (PartType)0);
            Assert.IsTrue(r.Success);
            Assert.IsTrue(r.OldPartBinned);
            Assert.AreEqual(10, r.ScrapAmount);
            Assert.AreEqual(GameRules.InventorySlots, inventory.Count);
            Assert.AreEqual(100, s.GetPart((PartType)0).Condition);
        }

        [Test]
        public void Preview_ChangesNothing()
        {
            Ship s = Docked(ShipType.Freighter, 160, 60, 35, 80, 80, 80, 80);
            sut.PreviewBuy(s, (PartType)0);
            sut.PreviewRelease(s);
            Assert.AreEqual(35, s.GetPart((PartType)0).Condition);
            Assert.AreEqual(0, inventory.Count);
            Assert.AreEqual(0, bays.GetBay(s));
        }

        [Test]
        public void Buy_FiresEventAfterStateChange()
        {
            Ship s = Docked(ShipType.Freighter, 160, 60, 35, 80, 80, 80, 80);
            BuyResult seen = null;
            int condAtEvent = -1;
            sut.PartBought += r => { seen = r; condAtEvent = s.GetPart((PartType)0).Condition; };
            sut.Buy(s, (PartType)0);
            Assert.IsNotNull(seen);
            Assert.AreEqual(100, condAtEvent);
            Assert.AreEqual(250, seen.Price);
        }

        [Test]
        public void Buy_RefusalFiresNoEvent()
        {
            Ship s = Docked(ShipType.Freighter, 160, 60, 80, 80, 80, 80, 80);
            int count = 0;
            sut.PartBought += r => count++;
            sut.Buy(s, (PartType)0);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Release_RepairedShip_NoFillsAndFullPayment()
        {
            Ship s = Docked(ShipType.Freighter, 160, 60, 60, 80, 80, 80, 80);
            ReleaseResult r = sut.Release(s);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(0, r.PenaltyFills);
            Assert.AreEqual(160, r.NetResult);
            Assert.IsTrue(bays.GetBay(s) < 0);
        }

        [Test]
        public void Release_WeakAndEmptySlots_AreFilledAtPenaltyPrice()
        {
            Ship s = Docked(ShipType.Fighter, 210, 70, 65, -1, 80, 80, 80);
            ReleasePreview p = sut.PreviewRelease(s);
            Assert.AreEqual(2, p.PenaltyFills);
            Assert.AreEqual(300, p.PenaltyPrice);
            Assert.AreEqual(210 - 600, p.NetResult);
            ReleaseResult r = sut.Release(s);
            Assert.AreEqual(p.NetResult, r.NetResult);
            Assert.AreEqual(2, r.FilledTypes.Length);
            Assert.AreEqual(100, s.GetPart((PartType)0).Condition);
            Assert.AreEqual(100, s.GetPart((PartType)1).Condition);
            Assert.AreEqual(0, inventory.Count);
        }

        [Test]
        public void Release_AllSlotsEmpty_StillWorks()
        {
            Ship s = Docked(ShipType.Freighter, 150, 60, -1, -1, -1, -1, -1);
            ReleaseResult r = sut.Release(s);
            Assert.IsTrue(r.Success);
            Assert.AreEqual(5, r.PenaltyFills);
            Assert.AreEqual(150 - 5 * 300, r.NetResult);
        }

        [Test]
        public void Release_NotDockedOrNull_IsRefused()
        {
            Assert.AreEqual(ReleaseRefusal.NullShip, sut.Release(null).Reason);
            Ship loose = MakeShip(ShipType.Freighter, 160, 60, 80, 80, 80, 80, 80);
            Assert.AreEqual(ReleaseRefusal.NotDocked, sut.Release(loose).Reason);
        }

        [Test]
        public void Release_FiresEventAfterBayFreed()
        {
            Ship s = Docked(ShipType.Freighter, 160, 60, 40, 80, 80, 80, 80);
            ReleaseResult seen = null;
            int bayAtEvent = 99;
            sut.ShipReleased += r => { seen = r; bayAtEvent = bays.GetBay(s); };
            sut.Release(s);
            Assert.IsNotNull(seen);
            Assert.AreEqual(-1, bayAtEvent);
            Assert.AreEqual(1, seen.PenaltyFills);
            Assert.AreEqual(160 - 300, seen.NetResult);
        }

        [Test]
        public void PenaltyPrice_RoundsHalfUp()
        {
            Assert.AreEqual(300, Prices.PenaltyPrice(balance));
            balance.newPartPrice = 155;
            Assert.AreEqual(186, Prices.PenaltyPrice(balance));
            balance.newPartPrice = 154;
            Assert.AreEqual(185, Prices.PenaltyPrice(balance));
            balance.newPartPrice = 3;
            balance.penaltyPercent = 50;
            Assert.AreEqual(5, Prices.PenaltyPrice(balance));
        }

        [Test]
        public void Buy_IsAlwaysCheaperThanPenaltyFill()
        {
            for (int price = 150; price <= 300; price++)
            {
                balance.newPartPrice = price;
                Assert.Less(Prices.NewPartPrice(balance), Prices.PenaltyPrice(balance));
            }
        }
    }
}
