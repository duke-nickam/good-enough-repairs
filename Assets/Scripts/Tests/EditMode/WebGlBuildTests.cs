using NUnit.Framework;
using UnityEngine;
using GoodEnoughRepairs.EditorTools;

namespace GoodEnoughRepairs.Tests
{
    public class WebGlBuildTests
    {
        private GameDataAsset asset;
        private TextAsset text;

        [SetUp]
        public void SetUp()
        {
            asset = ScriptableObject.CreateInstance<GameDataAsset>();
            text = new TextAsset("{}");
            asset.balance = text;
            asset.dayBriefs = text;
            asset.mechanicLines = text;
            asset.partCatalog = text;
            asset.uiText = text;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(asset);
            Object.DestroyImmediate(text);
        }

        [Test]
        public void AllFieldsSet_NoProblems()
        {
            Assert.AreEqual(0, WebGlFieldCheck.GameDataProblems(asset).Count);
        }

        [Test]
        public void MissingAsset_IsReported()
        {
            Assert.AreEqual(1, WebGlFieldCheck.GameDataProblems(null).Count);
        }

        [Test]
        public void MissingBalance_NamesField()
        {
            asset.balance = null;
            AssertOneNaming("balance");
        }

        [Test]
        public void MissingDayBriefs_NamesField()
        {
            asset.dayBriefs = null;
            AssertOneNaming("dayBriefs");
        }

        [Test]
        public void MissingMechanicLines_NamesField()
        {
            asset.mechanicLines = null;
            AssertOneNaming("mechanicLines");
        }

        [Test]
        public void MissingPartCatalog_NamesField()
        {
            asset.partCatalog = null;
            AssertOneNaming("partCatalog");
        }

        [Test]
        public void MissingUiText_NamesField()
        {
            asset.uiText = null;
            AssertOneNaming("uiText");
        }

        [Test]
        public void SeveralMissing_AllReported()
        {
            asset.balance = null;
            asset.uiText = null;
            Assert.AreEqual(2, WebGlFieldCheck.GameDataProblems(asset).Count);
        }

        [Test]
        public void Station_Set_NoProblems()
        {
            Assert.AreEqual(0, WebGlFieldCheck.StationProblems(true, asset).Count);
        }

        [Test]
        public void Station_GameDataUnset_NamesField()
        {
            var problems = WebGlFieldCheck.StationProblems(true, null);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("gameData", problems[0]);
        }

        [Test]
        public void Station_NotFound_IsReported()
        {
            var problems = WebGlFieldCheck.StationProblems(false, null);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("StationView", problems[0]);
        }

        private void AssertOneNaming(string field)
        {
            var problems = WebGlFieldCheck.GameDataProblems(asset);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("'" + field + "'", problems[0]);
        }
    }
}
