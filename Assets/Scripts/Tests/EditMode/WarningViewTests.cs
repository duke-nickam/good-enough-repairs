using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GoodEnoughRepairs.Tests
{
    /// <summary>
    /// Tests for the warning dialog: its message lines, the release path and the
    /// buy warning's mechanic line. The EditMode test assembly has no reference to
    /// the UX assembly, so WarningView is reached by name at run time instead of a
    /// compile-time reference.
    /// </summary>
    public sealed class WarningViewTests
    {
        private const string UxAssembly = "GoodEnoughRepairs.UX";
        private const string BuyLine = "TEST-BUY-LINE";
        private const string ReleaseLine = "TEST-RELEASE-LINE";

        private GameObject go;
        private DockingBays bays;
        private BuyRelease commands;
        private BalanceData balance;

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
        public void BuyWarningShowsPriceAndNoReleaseLines()
        {
            Assert.IsTrue(Applies("BuyPrice", true, false, 0, false));
            Assert.IsFalse(Applies("BuyBinned", true, false, 0, false));
            Assert.IsFalse(Applies("ReleasePayment", true, false, 0, true));
            Assert.IsFalse(Applies("ReleaseNet", true, false, 0, true));
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
        public void MechanicLineShowsOnBuyAndReleaseOnlyWithALine()
        {
            Assert.IsTrue(Applies("Mechanic", false, false, 1, true));
            Assert.IsTrue(Applies("Mechanic", true, false, 0, true));
            Assert.IsFalse(Applies("Mechanic", false, false, 1, false));
            Assert.IsFalse(Applies("Mechanic", true, false, 0, false));
        }

        private static ContentData MakeContent(bool withLines)
        {
            if (!withLines) return ContentData.Empty();
            MechanicLinesFile file = new MechanicLinesFile();
            file.moments = new MomentLines();
            file.moments.buy_warning = new string[] { BuyLine };
            file.moments.release_warning = new string[] { ReleaseLine };
            return new ContentData(file, null, null);
        }

        private object MakeView(out Ship ship, bool withLines)
        {
            balance = BalanceLoader.Load();
            ship = ShipFactory.Create(balance, balance.days[0], new System.Random(1));
            bays = new DockingBays();
            bays.TryDock(ship);
            commands = new BuyRelease(bays, new Inventory(), balance);
            go = new GameObject("WarningViewTest");
            Component view = go.AddComponent(ViewType());
            Call(view, "Bind", commands, MakeContent(withLines));
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

        private static string MechanicLineOf(object view)
        {
            PropertyInfo p = ViewType().GetProperty("MechanicLine", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(p, "MechanicLine not found");
            return (string)p.GetValue(view);
        }

        private static bool OpenAnyBuy(object view, Ship ship)
        {
            PartType[] types = PartTypes.All;
            for (int i = 0; i < types.Length; i++)
            {
                if ((bool)Call(view, "OpenBuy", ship, types[i])) return true;
            }
            return false;
        }

        [Test]
        public void OpenReleaseShowsReleaseLinesAndCloseHidesThem()
        {
            Ship ship;
            object view = MakeView(out ship, true);

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
            object view = MakeView(out ship, true);

            Assert.IsTrue(OpenAnyBuy(view, ship));
            Assert.IsTrue(Shown(view, "BuyPrice"));
            Assert.IsFalse(Shown(view, "BuyBinned"));
            Assert.IsFalse(Shown(view, "ReleasePayment"));
            Assert.IsFalse(Shown(view, "ReleaseNet"));
        }

        [Test]
        public void BuyWarningShowsTheMechanicLineFromContent()
        {
            Ship ship;
            object view = MakeView(out ship, true);

            Assert.IsTrue(OpenAnyBuy(view, ship));
            Assert.AreEqual(BuyLine, MechanicLineOf(view));
            Assert.IsTrue(Shown(view, "Mechanic"));
        }

        [Test]
        public void BuyWarningHasNoMechanicLineWhenContentHasNone()
        {
            Ship ship;
            object view = MakeView(out ship, false);

            Assert.IsTrue(OpenAnyBuy(view, ship));
            Assert.AreEqual("", MechanicLineOf(view));
            Assert.IsFalse(Shown(view, "Mechanic"));
        }

        [Test]
        public void ReleaseWarningShowsTheReleaseLineFromContent()
        {
            Ship ship;
            object view = MakeView(out ship, true);

            Assert.IsTrue((bool)Call(view, "OpenRelease", ship));
            Assert.AreEqual(ReleaseLine, MechanicLineOf(view));
        }

        [Test]
        public void ClosedWarningHasNoMechanicLine()
        {
            Ship ship;
            object view = MakeView(out ship, true);

            Assert.IsTrue(OpenAnyBuy(view, ship));
            Call(view, "Cancel");
            Assert.AreEqual("", MechanicLineOf(view));
        }

        [Test]
        public void ReleaseOfWeakShipWaitsForConfirm()
        {
            Ship ship;
            object view = MakeView(out ship, true);
            Assert.Greater(commands.PreviewRelease(ship).PenaltyFills, 0, "sample ship must be weak");

            Call(view, "RequestRelease", ship);

            Assert.IsTrue(IsOpen(view));
            Assert.AreEqual(0, bays.GetBay(ship), "the ship stays docked until Confirm");
        }

        [Test]
        public void CancelKeepsTheShipDocked()
        {
            Ship ship;
            object view = MakeView(out ship, true);
            Call(view, "RequestRelease", ship);

            Call(view, "Cancel");

            Assert.IsFalse(IsOpen(view));
            Assert.AreEqual(0, bays.GetBay(ship));
            Assert.Greater(commands.PreviewRelease(ship).PenaltyFills, 0);
        }

        [Test]
        public void ConfirmReleasesThroughTheReleaseCommand()
        {
            Ship ship;
            object view = MakeView(out ship, true);
            ReleaseResult seen = null;
            commands.ShipReleased += r => seen = r;
            ReleasePreview preview = commands.PreviewRelease(ship);
            Call(view, "RequestRelease", ship);

            Call(view, "Confirm");

            Assert.IsFalse(IsOpen(view));
            Assert.IsNotNull(seen, "the release command ran");
            Assert.AreEqual(preview.NetResult, seen.NetResult);
            Assert.AreEqual(preview.PenaltyFills, seen.PenaltyFills);
            Assert.AreEqual(-1, bays.GetBay(ship));
        }

        [Test]
        public void RepairedShipReleasesAtOnceThroughTheCommand()
        {
            Ship ship;
            object view = MakeView(out ship, true);
            PartType[] types = PartTypes.All;
            for (int i = 0; i < types.Length; i++) commands.Buy(ship, types[i]);
            Assert.AreEqual(0, commands.PreviewRelease(ship).PenaltyFills);
            ReleaseResult seen = null;
            commands.ShipReleased += r => seen = r;

            Call(view, "RequestRelease", ship);

            Assert.IsFalse(IsOpen(view));
            Assert.IsNotNull(seen);
            Assert.AreEqual(ship.Payment, seen.NetResult);
            Assert.AreEqual(-1, bays.GetBay(ship));
        }
    }
}
