using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GoodEnoughRepairs.Tests
{
    /// <summary>
    /// Tests for the warning dialog's message lines. The EditMode test assembly
    /// has no reference to the UX assembly, so WarningView is reached by name
    /// at run time instead of a compile-time reference.
    /// </summary>
    public sealed class WarningViewTests
    {
        private const string UxAssembly = "GoodEnoughRepairs.UX";

        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null) UnityEngine.Object.DestroyImmediate(go);
            go = null;
        }

        private static Type ViewType()
        {
            Type t = Type.GetType("GoodEnoughRepairs.WarningView, " + UxAssembly);
            Assert.IsNotNull(t, "WarningView type not found");
            return t;
        }

        private static Type LineType()
        {
            Type t = Type.GetType("GoodEnoughRepairs.WarningLine, " + UxAssembly);
            Assert.IsNotNull(t, "WarningLine type not found");
            return t;
        }

        private static object Line(string name)
        {
            return Enum.Parse(LineType(), name);
        }

        private static bool Applies(string line, bool isBuy, bool binned, int fills, bool hasMechanic)
        {
            MethodInfo m = ViewType().GetMethod("LineApplies", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(m, "LineApplies not found");
            return (bool)m.Invoke(null, new object[] { Line(line), isBuy, binned, fills, hasMechanic });
        }

        [Test]
        public void BuyWarningShowsPriceOnly()
        {
            Assert.IsTrue(Applies("BuyPrice", true, false, 0, false));
            Assert.IsFalse(Applies("BuyBinned", true, false, 0, false));
            Assert.IsFalse(Applies("ReleasePayment", true, false, 0, true));
            Assert.IsFalse(Applies("ReleaseNet", true, false, 0, true));
            Assert.IsFalse(Applies("Mechanic", true, false, 0, true));
        }

        [Test]
        public void BuyWarningShowsBinnedLineWhenOldPartIsBinned()
        {
            Assert.IsTrue(Applies("BuyBinned", true, true, 0, false));
        }

        [Test]
        public void ReleaseWarningShowsPaymentAndNet()
        {
            Assert.IsFalse(Applies("BuyPrice", false, false, 2, true));
            Assert.IsFalse(Applies("BuyBinned", false, true, 2, true));
            Assert.IsTrue(Applies("ReleasePayment", false, false, 0, false));
            Assert.IsTrue(Applies("ReleaseNet", false, false, 0, false));
        }

        [Test]
        public void PenaltyLineOnlyWhenPartsAreFitted()
        {
            Assert.IsFalse(Applies("ReleasePenalty", false, false, 0, true));
            Assert.IsTrue(Applies("ReleasePenalty", false, false, 1, true));
        }

        [Test]
        public void MechanicLineOnlyOnReleaseWithALine()
        {
            Assert.IsTrue(Applies("Mechanic", false, false, 1, true));
            Assert.IsFalse(Applies("Mechanic", false, false, 1, false));
        }

        private object MakeView(out Ship ship)
        {
            BalanceData balance = BalanceLoader.Load();
            ship = ShipFactory.Create(balance, balance.days[0], new System.Random(1));
            DockingBays bays = new DockingBays();
            bays.TryDock(ship);
            BuyRelease commands = new BuyRelease(bays, new Inventory(), balance);
            go = new GameObject("WarningViewTest");
            Component view = go.AddComponent(ViewType());
            Call(view, "Bind", commands, ContentLoader.Load());
            return view;
        }

        private static object Call(object target, string method, params object[] args)
        {
            MethodInfo m = ViewType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(m, method + " not found");
            return m.Invoke(target, args);
        }

        private static bool Shown(object view, string line)
        {
            return (bool)Call(view, "IsLineShown", Line(line));
        }

        private static bool IsOpen(object view)
        {
            PropertyInfo p = ViewType().GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(p, "IsOpen not found");
            return (bool)p.GetValue(view);
        }

        [Test]
        public void OpenReleaseShowsReleaseLinesAndCloseHidesThem()
        {
            Ship ship;
            object view = MakeView(out ship);

            Assert.IsTrue((bool)Call(view, "OpenRelease", ship));
            Assert.IsTrue(Shown(view, "ReleasePayment"));
            Assert.IsTrue(Shown(view, "ReleaseNet"));
            Assert.IsFalse(Shown(view, "BuyPrice"));
            Assert.IsFalse(Shown(view, "BuyBinned"));

            Call(view, "Cancel");
            Assert.IsFalse(IsOpen(view));
            Assert.IsFalse(Shown(view, "ReleasePayment"));
        }

        [Test]
        public void OpenBuyShowsPriceAndNotReleaseLines()
        {
            Ship ship;
            object view = MakeView(out ship);

            bool opened = false;
            PartType[] types = PartTypes.All;
            for (int i = 0; i < types.Length && !opened; i++)
            {
                opened = (bool)Call(view, "OpenBuy", ship, types[i]);
            }
            Assert.IsTrue(opened);
            Assert.IsTrue(Shown(view, "BuyPrice"));
            Assert.IsFalse(Shown(view, "BuyBinned"));
            Assert.IsFalse(Shown(view, "ReleasePayment"));
            Assert.IsFalse(Shown(view, "ReleaseNet"));
            Assert.IsFalse(Shown(view, "Mechanic"));
        }
    }
}
