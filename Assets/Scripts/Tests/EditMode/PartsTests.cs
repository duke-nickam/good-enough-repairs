using System;
using System.IO;
using NUnit.Framework;

namespace GoodEnoughRepairs
{
    public class PartsTests
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

        [Test]
        public void Passes_AtFreighterLevel()
        {
            int level = LoadBalance().requiredFreighterPercent;
            Assert.IsTrue(new Part(PartType.Engine, level).Passes(level));
        }

        [Test]
        public void Fails_OneBelowFreighterLevel()
        {
            int level = LoadBalance().requiredFreighterPercent;
            Assert.IsFalse(new Part(PartType.Engine, level - 1).Passes(level));
        }

        [Test]
        public void Passes_AtFighterLevel()
        {
            int level = LoadBalance().requiredFighterPercent;
            Assert.IsTrue(new Part(PartType.Thruster, level).Passes(level));
        }

        [Test]
        public void Fails_OneBelowFighterLevel()
        {
            int level = LoadBalance().requiredFighterPercent;
            Assert.IsFalse(new Part(PartType.Thruster, level - 1).Passes(level));
        }

        [Test]
        public void FighterWeakPart_CanStillPassFreighter()
        {
            BalanceData b = LoadBalance();
            Part p = new Part(PartType.Engine, b.requiredFighterPercent - 1);
            Assert.IsFalse(p.Passes(b.requiredFighterPercent));
            Assert.IsTrue(p.Passes(b.requiredFreighterPercent));
        }

        [Test]
        public void Condition_IsClampedTo0To100()
        {
            Assert.AreEqual(0, new Part(PartType.Engine, -5).Condition);
            Assert.AreEqual(100, new Part(PartType.Engine, 140).Condition);
            Assert.AreEqual(100, PartGenerator.Create(PartType.PowerCore, 100).Condition);
        }

        [Test]
        public void SameSeed_GivesSamePart()
        {
            Part a = PartGenerator.Create(new Random(42), PartType.HullPlate, 40, 90);
            Part b = PartGenerator.Create(new Random(42), PartType.HullPlate, 40, 90);
            Assert.AreEqual(a.Type, b.Type);
            Assert.AreEqual(a.Condition, b.Condition);
        }

        [Test]
        public void SameSeed_GivesSameSequence()
        {
            Random r1 = new Random(7);
            Random r2 = new Random(7);
            for (int i = 0; i < 50; i++)
            {
                Part a = PartGenerator.CreateRandomType(r1, 0, 100);
                Part b = PartGenerator.CreateRandomType(r2, 0, 100);
                Assert.AreEqual(a.Type, b.Type);
                Assert.AreEqual(a.Condition, b.Condition);
            }
        }

        [Test]
        public void Generated_StaysInRange()
        {
            Random rng = new Random(1);
            for (int i = 0; i < 500; i++)
            {
                int c = PartGenerator.Create(rng, PartType.Engine, 62, 69).Condition;
                Assert.GreaterOrEqual(c, 62);
                Assert.LessOrEqual(c, 69);
            }
        }

        [Test]
        public void BadRange_IsSwappedAndClamped()
        {
            Random rng = new Random(3);
            for (int i = 0; i < 200; i++)
            {
                int c = PartGenerator.Create(rng, PartType.Engine, 90, 80).Condition;
                Assert.GreaterOrEqual(c, 80);
                Assert.LessOrEqual(c, 90);
                int d = PartGenerator.Create(rng, PartType.Engine, -20, 300).Condition;
                Assert.GreaterOrEqual(d, 0);
                Assert.LessOrEqual(d, 100);
            }
        }

        [Test]
        public void FivePartTypes_WithCatalogKeys()
        {
            PartType[] all = PartTypes.All;
            Assert.AreEqual(5, all.Length);
            Assert.AreEqual("engine", PartTypes.Key(PartType.Engine));
            Assert.AreEqual("hull_plate", PartTypes.Key(PartType.HullPlate));
            Assert.AreEqual("shield_generator", PartTypes.Key(PartType.ShieldGenerator));
            Assert.AreEqual("thruster", PartTypes.Key(PartType.Thruster));
            Assert.AreEqual("power_core", PartTypes.Key(PartType.PowerCore));
        }
    }
}
