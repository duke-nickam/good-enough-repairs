using NUnit.Framework;

namespace GoodEnoughRepairs
{
    public class GameRulesTests
    {
        [Test]
        public void FixedGddRules_HaveTheGddValues()
        {
            Assert.AreEqual(3, GameRules.DockingBays);
            Assert.AreEqual(10, GameRules.InventorySlots);
            Assert.AreEqual(15, GameRules.JunkyardParts);
            Assert.AreEqual(7, GameRules.RunDays);
            Assert.AreEqual(5, GameRules.PartSlotsPerShip);
        }
    }
}
