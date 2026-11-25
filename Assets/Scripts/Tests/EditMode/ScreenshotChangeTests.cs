using NUnit.Framework;
using GoodEnoughRepairs.EditorTools;

namespace GoodEnoughRepairs.Tests
{
    public class ScreenshotChangeTests
    {
        private const string Spec = "Assets/Prefabs/UX/ship_in_bay.spec.json";

        [Test]
        public void ChangedSpecWithEarlierShotIsPhotographed()
        {
            Assert.IsTrue(ScreenshotDecision.ShouldPhotograph(true, true, true));
        }

        [Test]
        public void ChangedSpecWithNoEarlierShotIsPhotographed()
        {
            Assert.IsTrue(ScreenshotDecision.ShouldPhotograph(true, true, false));
        }

        [Test]
        public void UnchangedSpecWithEarlierShotIsSkipped()
        {
            Assert.IsFalse(ScreenshotDecision.ShouldPhotograph(true, false, true));
        }

        [Test]
        public void UnchangedSpecWithNoEarlierShotIsStillPhotographed()
        {
            Assert.IsTrue(ScreenshotDecision.ShouldPhotograph(true, false, false));
        }

        [Test]
        public void SpecWithoutScreenshotIsNeverPhotographed()
        {
            Assert.IsFalse(ScreenshotDecision.ShouldPhotograph(false, true, false));
            Assert.IsFalse(ScreenshotDecision.ShouldPhotograph(false, false, false));
            Assert.IsFalse(ScreenshotDecision.ShouldPhotograph(false, false, true));
        }

        [Test]
        public void SkippedLineNamesTheSpec()
        {
            string line = ScreenshotDecision.BuildLine(Spec, false, false);
            Assert.AreEqual("SKIPPED (unchanged): " + Spec, line);
        }

        [Test]
        public void BuiltLinesNameTheSpec()
        {
            Assert.AreEqual("BUILT: " + Spec + " (screenshot taken)", ScreenshotDecision.BuildLine(Spec, true, true));
            Assert.AreEqual("BUILT: " + Spec + " (no screenshot)", ScreenshotDecision.BuildLine(Spec, true, false));
        }

        [Test]
        public void PhotographedUnchangedSpecIsReportedAsBuilt()
        {
            string line = ScreenshotDecision.BuildLine(Spec, false, true);
            Assert.AreEqual("BUILT: " + Spec + " (screenshot taken)", line);
        }

        [Test]
        public void OnlyTheChangedSpecIsPhotographedWhenAllShotsExist()
        {
            // Three specs, all with an earlier shot, only the second one changed.
            bool[] changed = { false, true, false };
            int photographed = 0;
            for (int i = 0; i < changed.Length; i++)
            {
                if (ScreenshotDecision.ShouldPhotograph(true, changed[i], true)) photographed++;
            }
            Assert.AreEqual(1, photographed);
        }

        [Test]
        public void NoSpecIsPhotographedWhenNothingChangedAndAllShotsExist()
        {
            for (int i = 0; i < 3; i++)
            {
                Assert.IsFalse(ScreenshotDecision.ShouldPhotograph(true, false, true));
                StringAssert.StartsWith("SKIPPED (unchanged): ", ScreenshotDecision.BuildLine(Spec, false, false));
            }
        }
    }
}
