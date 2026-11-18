using NUnit.Framework;

namespace GoodEnoughRepairs
{
    public class BarBandTests
    {
        private const int Required = 60;
        private const int Margin = 10;

        [Test]
        public void BelowRequired_IsRed()
        {
            Assert.AreEqual(BarBandKind.Red, BarBand.Of(35, Required, Margin));
            Assert.AreEqual(BarBandKind.Red, BarBand.Of(59, Required, Margin));
            Assert.AreEqual(BarBandKind.Red, BarBand.Of(0, Required, Margin));
        }

        [Test]
        public void ExactlyRequired_IsGreen()
        {
            Assert.AreEqual(BarBandKind.Green, BarBand.Of(60, Required, Margin));
        }

        [Test]
        public void PassingButNotSurplus_IsGreen()
        {
            Assert.AreEqual(BarBandKind.Green, BarBand.Of(65, Required, Margin));
            Assert.AreEqual(BarBandKind.Green, BarBand.Of(69, Required, Margin));
        }

        [Test]
        public void ExactlyRequiredPlusMargin_IsBlue()
        {
            Assert.AreEqual(BarBandKind.Blue, BarBand.Of(70, Required, Margin));
        }

        [Test]
        public void Surplus_IsBlue()
        {
            Assert.AreEqual(BarBandKind.Blue, BarBand.Of(85, Required, Margin));
            Assert.AreEqual(BarBandKind.Blue, BarBand.Of(100, Required, Margin));
        }

        [Test]
        public void MissingPart_IsRed()
        {
            Assert.AreEqual(BarBandKind.Red, BarBand.Of((Part)null, Required, Margin));
        }

        [Test]
        public void InstalledPart_UsesItsCondition()
        {
            Assert.AreEqual(BarBandKind.Red, BarBand.Of(new Part(PartType.Engine, 40), Required, Margin));
            Assert.AreEqual(BarBandKind.Green, BarBand.Of(new Part(PartType.Engine, 62), Required, Margin));
            Assert.AreEqual(BarBandKind.Blue, BarBand.Of(new Part(PartType.Engine, 90), Required, Margin));
        }

        [Test]
        public void SurplusMargin_IsFighterMinusFreighter()
        {
            Assert.AreEqual(10, BarBand.SurplusMargin(70, 60));
        }

        [Test]
        public void SurplusMargin_NeverNegative()
        {
            Assert.AreEqual(0, BarBand.SurplusMargin(60, 70));
        }
    }
}
