using System;
using System.IO;
using GoodEnoughRepairs.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace GoodEnoughRepairs.Tests
{
    public class UiTextTests
    {
        private static ContentData Make(params string[] keyAndText)
        {
            UiTextEntry[] entries = new UiTextEntry[keyAndText.Length / 2];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new UiTextEntry { key = keyAndText[i * 2], text = keyAndText[i * 2 + 1] };
            }
            return new ContentData(null, null, null, null, new UiTextFile { entries = entries });
        }

        [Test]
        public void Lookup_FindsKey()
        {
            ContentData c = Make("release_button", "Release ship");
            Assert.AreEqual("Release ship", c.Text("release_button"));
            Assert.IsTrue(c.HasText("release_button"));
        }

        [Test]
        public void Lookup_MissingKey_ShowsKeyInBrackets()
        {
            ContentData c = Make("a", "A");
            Assert.AreEqual("[nope]", c.Text("nope"));
            Assert.IsFalse(c.HasText("nope"));
        }

        [Test]
        public void Lookup_NullOrEmptyKey_DoesNotThrow()
        {
            ContentData c = Make("a", "A");
            Assert.AreEqual("[]", c.Text(null));
            Assert.AreEqual("[]", c.Text(""));
        }

        [Test]
        public void Lookup_WithNoTextLoaded_FallsBack()
        {
            ContentData c = ContentData.Empty();
            Assert.AreEqual("[credits]", c.Text("credits", 5));
        }

        [Test]
        public void Fill_OneNumber()
        {
            ContentData c = Make("credits", "Credits: {0}");
            Assert.AreEqual("Credits: 120", c.Text("credits", 120));
        }

        [Test]
        public void Fill_TwoNumbers()
        {
            ContentData c = Make("weakest", "Weakest: {0} {1}%");
            Assert.AreEqual("Weakest: Engine 35%", c.Text("weakest", "Engine", 35));
        }

        [Test]
        public void Fill_SameNumberTwice_AndOutOfOrder()
        {
            Assert.AreEqual("b a b", ContentData.Fill("{1} {0} {1}", "a", "b"));
        }

        [Test]
        public void Fill_MissingNumber_StaysAsWritten()
        {
            ContentData c = Make("weakest", "Weakest: {0} {1}%");
            Assert.AreEqual("Weakest: Engine {1}%", c.Text("weakest", "Engine"));
            Assert.AreEqual("Payment: {0}", Make("payment", "Payment: {0}").Text("payment"));
        }

        [Test]
        public void Fill_ExtraNumbers_AreIgnored()
        {
            Assert.AreEqual("Needs 60%", ContentData.Fill("Needs {0}%", 60, 70, 80));
        }

        [Test]
        public void Fill_NullArgument_GivesEmptyText()
        {
            Assert.AreEqual("Payment: ", ContentData.Fill("Payment: {0}", new object[] { null }));
        }

        [Test]
        public void Fill_BadPlaceholders_StayAsWritten()
        {
            Assert.AreEqual("{} {x} {-1} {0", ContentData.Fill("{} {x} {-1} {0", 5));
        }

        [Test]
        public void Fill_NullOrEmptyTemplate_GivesEmpty()
        {
            Assert.AreEqual("", ContentData.Fill(null, 1));
            Assert.AreEqual("", ContentData.Fill("", 1));
        }

        [Test]
        public void Keys_PartAndShipType()
        {
            Assert.AreEqual("part_hull_plate", UiKeys.PartKey(PartType.HullPlate));
            Assert.AreEqual("part_power_core", UiKeys.PartKey(PartType.PowerCore));
            Assert.AreEqual("type_fighter", UiKeys.ShipTypeKey(ShipType.Fighter));
            Assert.AreEqual("type_freighter", UiKeys.ShipTypeKey(ShipType.Freighter));
        }

        [Test]
        public void ParseUiText_ReadsEntries()
        {
            string error;
            UiTextFile f = ContentLoader.ParseUiText(
                "{ \"entries\": [ { \"key\": \"a\", \"text\": \"A {0}\" } ] }", out error);
            Assert.IsNotNull(f, error);
            ContentData c = new ContentData(null, null, null, null, f);
            Assert.AreEqual("A 1", c.Text("a", 1));
        }

        [Test]
        public void ParseUiText_BadInput_ReturnsNullWithError()
        {
            string error;
            Assert.IsNull(ContentLoader.ParseUiText("", out error));
            Assert.IsNotEmpty(error);
            Assert.IsNull(ContentLoader.ParseUiText("{ \"entries\": [] }", out error));
            Assert.IsNotEmpty(error);
            Assert.IsNull(ContentLoader.ParseUiText("{ \"other\": 1 }", out error));
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void RealFile_HasEveryKeyTheViewsUse()
        {
            string path = Path.Combine(Application.dataPath, "GameData/content/ui_text.json");
            Assert.IsTrue(File.Exists(path), path);
            string error;
            UiTextFile f = ContentLoader.ParseUiText(File.ReadAllText(path), out error);
            Assert.IsNotNull(f, error);
            ContentData c = new ContentData(null, null, null, null, f);

            string[] keys =
            {
                UiKeys.StationTitle, UiKeys.Credits, UiKeys.Payment, UiKeys.ShipsLeft, UiKeys.DayOver,
                UiKeys.BayLabel, UiKeys.BayEmpty, UiKeys.Needs, UiKeys.Weakest, UiKeys.TypeFighter,
                UiKeys.TypeFreighter, UiKeys.PartEngine, UiKeys.PartHullPlate, UiKeys.PartShieldGenerator,
                UiKeys.PartThruster, UiKeys.PartPowerCore, UiKeys.PartEmpty, UiKeys.ReleaseButton,
                UiKeys.BuyButton, UiKeys.InventoryTitle, UiKeys.InventoryFull, UiKeys.BinLabel,
                UiKeys.BinHint, UiKeys.BinButton, UiKeys.BuyTitle, UiKeys.BuyPrice, UiKeys.BuyBinned,
                UiKeys.ConfirmBuy, UiKeys.CancelBuy, UiKeys.ReleaseTitle, UiKeys.ReleasePayment,
                UiKeys.ReleasePenalty, UiKeys.ReleaseNet, UiKeys.ConfirmRelease, UiKeys.CancelRelease
            };
            for (int i = 0; i < keys.Length; i++)
            {
                Assert.IsTrue(c.HasText(keys[i]), "missing key in ui_text.json: " + keys[i]);
            }
        }

        // ---- the builder's text fill (through the builder, so the test needs no
        // reference to the UX assembly)

        [Test]
        public void BuilderTextFill_BeforeContentIsLoaded_ShowsKeyInBrackets()
        {
            Assert.AreEqual("[summary_continue]",
                PrefabSpecBuilder.FillTextSample("summary_continue", false, false));
        }

        [Test]
        public void BuilderTextFill_AfterRefresh_ResolvesKeysOfRealFile()
        {
            Assert.AreEqual("Continue", PrefabSpecBuilder.FillTextSample("summary_continue", true, false));
            Assert.AreEqual("Play again", PrefabSpecBuilder.FillTextSample("run_end_play_again", true, false));
        }

        [Test]
        public void BuilderTextFill_HiddenPanelText_IsResolvedToo()
        {
            string title = PrefabSpecBuilder.FillTextSample("summary_title", true, true);
            Assert.AreEqual("End of day", title);
            string note = PrefabSpecBuilder.FillTextSample("summary_note", true, true);
            Assert.IsFalse(note.StartsWith("["), note);
            Assert.IsTrue(note.Contains("balance"), note);
        }

        [Test]
        public void ShotIsStale_MissingShot_IsStale()
        {
            string missing = Path.Combine(Path.GetTempPath(), "ger_test_missing_" + Guid.NewGuid().ToString("N") + ".png");
            Assert.IsTrue(PrefabSpecBuilder.ShotIsStale(missing, new string[0]));
        }

        [Test]
        public void ShotIsStale_NewerDataFile_IsStale_OlderIsNot()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ger_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string shot = Path.Combine(dir, "shot.png");
                string data = Path.Combine(dir, "ui_text.json");
                File.WriteAllText(shot, "x");
                File.WriteAllText(data, "{}");
                DateTime now = DateTime.UtcNow;

                File.SetLastWriteTimeUtc(shot, now);
                File.SetLastWriteTimeUtc(data, now.AddMinutes(-5));
                Assert.IsFalse(PrefabSpecBuilder.ShotIsStale(shot, new[] { data }));

                File.SetLastWriteTimeUtc(data, now.AddMinutes(5));
                Assert.IsTrue(PrefabSpecBuilder.ShotIsStale(shot, new[] { data }));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
