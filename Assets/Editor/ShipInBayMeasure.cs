/*
 * SHIP IN BAY MEASURE
 *
 * Batch-mode check of the ship in bay 1. It builds Assets/Prefabs/UX/ship_in_bay.spec.json
 * into Main.unity with PrefabSpecBuilder, shows the sample ship (day 1, seed 1, with the
 * spec's sampleConditions applied) and measures it in screen pixels at the spec's
 * reference size.
 *
 * HOW TO RUN
 *   Unity -batchmode -projectPath <project> -quit
 *     -executeMethod GoodEnoughRepairs.EditorTools.ShipInBayMeasure.Run
 *
 * OUTPUT
 *   TOLERANCE: 1 px
 *   MEASURE <name>: <measured> expected <expected> PASS|FAIL
 *   RESULT: PASS or RESULT: FAIL (last line)
 * Exit code 0 on PASS, 1 on FAIL, a failed build, or a missing object.
 *
 * MEASUREMENTS
 *   1. The center of the ship's content bounds (body and bars, ShipView.TryGetContentBounds)
 *      against the center of the spec's screen rectangle (bay 1).
 *   2. For the first sample part of each band (red, green, blue), judged with BarBand
 *      against the sample ship's required level: the fill ratio (fill width / track
 *      width) against condition / 100, within the tolerance in pixels, and the fill
 *      color against the band's color.
 *
 * Where values come from: rectangle and sample conditions from the spec, the required
 * level from the generated ship. The scene is not saved after the sample ship is shown.
 */

using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GoodEnoughRepairs.EditorTools
{
    public static class ShipInBayMeasure
    {
        private const string SpecPath = "Assets/Prefabs/UX/ship_in_bay.spec.json";
        private const float TolerancePx = 1f;

        private static bool failed;

        /// <summary>Batch entry method.</summary>
        public static void Run()
        {
            failed = false;
            int code;
            try
            {
                Measure();
            }
            catch (Exception e)
            {
                Debug.Log("MEASURE error: " + e.Message + " expected no error FAIL");
                failed = true;
            }
            Debug.Log(failed ? "RESULT: FAIL" : "RESULT: PASS");
            code = failed ? 1 : 0;
            EditorApplication.Exit(code);
        }

        private static string N(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static void Line(string name, string measured, string expected, bool pass)
        {
            if (!pass) failed = true;
            Debug.Log("MEASURE " + name + ": " + measured + " expected " + expected + (pass ? " PASS" : " FAIL"));
        }

        private static void Missing(string name, string what)
        {
            Line(name, "missing (" + what + ")", "present", false);
        }

        private static int PartIndex(string name)
        {
            PartType type;
            switch (name)
            {
                case "Engine": type = PartType.Engine; break;
                case "HullPlate": type = PartType.HullPlate; break;
                case "ShieldGenerator": type = PartType.ShieldGenerator; break;
                case "Thruster": type = PartType.Thruster; break;
                case "PowerCore": type = PartType.PowerCore; break;
                default: return -1;
            }
            PartType[] all = PartTypes.All;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == type) return i;
            }
            return -1;
        }

        private static Vector2 ToPx(Camera cam, Vector3 world, float refW, float refH)
        {
            Vector3 v = cam.WorldToViewportPoint(world);
            return new Vector2(v.x * refW, (1f - v.y) * refH);
        }

        private static float WidthPx(Camera cam, Bounds b, float refW, float refH)
        {
            Vector2 a = ToPx(cam, b.min, refW, refH);
            Vector2 c = ToPx(cam, b.max, refW, refH);
            return Mathf.Abs(c.x - a.x);
        }

        private static void Measure()
        {
            Debug.Log("TOLERANCE: " + N(TolerancePx) + " px");

            if (!File.Exists(SpecPath)) { Missing("spec", SpecPath); return; }
            PrefabSpec spec = JsonUtility.FromJson<PrefabSpec>(File.ReadAllText(SpecPath));
            if (spec == null || spec.scene == null || spec.root == null) { Missing("spec scene", SpecPath); return; }
            SpecScene sc = spec.scene;
            if (sc.rectW <= 0f || sc.refWidth <= 0f || sc.refHeight <= 0f)
            {
                Missing("bay 1 rectangle", "scene.rectW, refWidth, refHeight");
                return;
            }

            PrefabSpecBuilder.Build(SpecPath);

            ShipView view = null;
            ShipView[] views = UnityEngine.Object.FindObjectsByType<ShipView>(FindObjectsSortMode.None);
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i].gameObject.name == spec.root.name) view = views[i];
            }
            if (view == null) { Missing("ship", spec.root.name + " in " + sc.path); return; }

            Camera cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null) { Missing("camera", "scene camera"); return; }
            cam.aspect = sc.refWidth / sc.refHeight;

            try
            {
                MeasureView(spec, sc, view, cam);
            }
            finally
            {
                cam.ResetAspect();
            }
        }

        private static void MeasureView(PrefabSpec spec, SpecScene sc, ShipView view, Camera cam)
        {
            BalanceData balance = BalanceLoader.Load();
            if (balance == null || balance.days == null || balance.days.Length == 0)
            {
                Missing("balance", "balance.json");
                return;
            }
            int margin = BarBand.SurplusMargin(balance.requiredFighterPercent, balance.requiredFreighterPercent);
            Ship ship = ShipFactory.Create(balance, balance.days[0], new System.Random(1));
            DockingBays bays = new DockingBays();
            bays.TryDock(ship);

            int[] overrides = new int[PartTypes.All.Length];
            for (int i = 0; i < overrides.Length; i++) overrides[i] = -1;
            if (sc.sampleConditions != null)
            {
                for (int i = 0; i < sc.sampleConditions.Length; i++)
                {
                    SpecPartCondition c = sc.sampleConditions[i];
                    if (c == null) continue;
                    int idx = PartIndex(c.part);
                    if (idx >= 0) overrides[idx] = c.condition;
                }
            }
            view.Show(ship, margin, overrides);

            // 1. Ship center against bay 1 center.
            Bounds bounds;
            if (!view.TryGetContentBounds(out bounds))
            {
                Missing("ship center", "content bounds");
            }
            else
            {
                Vector2 got = ToPx(cam, bounds.center, sc.refWidth, sc.refHeight);
                Vector2 want = new Vector2(sc.rectX + sc.rectW / 2f, sc.rectY + sc.rectH / 2f);
                bool ok = Mathf.Abs(got.x - want.x) <= TolerancePx && Mathf.Abs(got.y - want.y) <= TolerancePx;
                Line("ship center vs bay 1 center (px x,y)", N(got.x) + "," + N(got.y),
                    N(want.x) + "," + N(want.y), ok);
            }

            // 2. One bar per band, from the spec's sample conditions.
            BarBandKind[] bands = { BarBandKind.Red, BarBandKind.Green, BarBandKind.Blue };
            string[] names = { "red", "green", "blue" };
            for (int b = 0; b < bands.Length; b++)
            {
                SpecPartCondition pick = null;
                int pickIndex = -1;
                if (sc.sampleConditions != null)
                {
                    for (int i = 0; i < sc.sampleConditions.Length && pick == null; i++)
                    {
                        SpecPartCondition c = sc.sampleConditions[i];
                        if (c == null) continue;
                        int idx = PartIndex(c.part);
                        if (idx < 0) continue;
                        if (BarBand.Of(c.condition, ship.RequiredLevel, margin) == bands[b])
                        {
                            pick = c;
                            pickIndex = idx;
                        }
                    }
                }
                if (pick == null) { Missing(names[b] + " bar", "no sample condition in this band"); continue; }

                string label = names[b] + " bar " + pick.part;
                SpriteRenderer fill = view.barFills != null && pickIndex < view.barFills.Length
                    ? view.barFills[pickIndex] : null;
                if (fill == null) { Missing(label, "bar fill"); continue; }

                Transform trackT = view.transform.Find("BarTrack_" + pick.part);
                SpriteRenderer track = trackT == null ? null : trackT.GetComponent<SpriteRenderer>();
                if (track == null) { Missing(label, "BarTrack_" + pick.part); continue; }

                float fullPx = WidthPx(cam, track.bounds, sc.refWidth, sc.refHeight);
                float fillPx = WidthPx(cam, fill.bounds, sc.refWidth, sc.refHeight);
                float expectedRatio = pick.condition / (float)Part.MaxCondition;
                float measuredRatio = fullPx > 0f ? fillPx / fullPx : 0f;
                bool fillOk = fullPx > 0f && Mathf.Abs(fillPx - expectedRatio * fullPx) <= TolerancePx;
                Line(label + " fill ratio (" + N(fillPx) + " of " + N(fullPx) + " px)",
                    N(measuredRatio), N(expectedRatio), fillOk);

                Color32 gotColor = fill.color;
                Color32 wantColor = BarColors.ColorOf(bands[b], view.belowColor, view.passesColor, view.surplusColor);
                bool colorOk = gotColor.r == wantColor.r && gotColor.g == wantColor.g &&
                    gotColor.b == wantColor.b && gotColor.a == wantColor.a;
                Line(label + " color", "#" + ColorUtility.ToHtmlStringRGBA(gotColor),
                    "#" + ColorUtility.ToHtmlStringRGBA(wantColor) + " (" + names[b] + ")", colorOk);
            }
        }
    }
}
