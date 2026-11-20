using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GoodEnoughRepairs
{
    // Mirrors the JSON files in Assets/StreamingAssets/content key for key.
    // Plain data only: no Unity types, no game rules.

    // mechanic_lines.json: "moments" is an object with one array per moment key.
    // JsonUtility cannot read dictionaries, so every key is a field.
    [Serializable]
    public class MomentLines
    {
        public string[] day_start;
        public string[] release_warning;
        public string[] buy_warning;
        public string[] penalty_fill;
        public string[] day_end;
        public string[] bankruptcy;
        public string[] run_end_win;
        public string[] run_end_bankrupt;
        public string[] tutorial_hint;
        public string[] fallback_hint;
    }

    [Serializable]
    public class MechanicLinesFile
    {
        public MomentLines moments;
    }

    // day_briefs.json
    [Serializable]
    public class DayBrief
    {
        public int day;
        public string type;
        public string reason;
    }

    [Serializable]
    public class DayBriefsFile
    {
        public DayBrief[] days;
    }

    // part_catalog.json
    [Serializable]
    public class PartInfo
    {
        public string type;
        public string name;
        public string description;
    }

    [Serializable]
    public class PartCatalogFile
    {
        public PartInfo[] parts;
    }

    // ui_text.json: a list of key and text pairs (no dictionaries in JsonUtility).
    [Serializable]
    public class UiTextEntry
    {
        public string key;
        public string text;
    }

    [Serializable]
    public class UiTextFile
    {
        public UiTextEntry[] entries;
    }

    // Moment keys, as written in mechanic_lines.json.
    public static class MomentKeys
    {
        public const string DayStart = "day_start";
        public const string ReleaseWarning = "release_warning";
        public const string BuyWarning = "buy_warning";
        public const string PenaltyFill = "penalty_fill";
        public const string DayEnd = "day_end";
        public const string Bankruptcy = "bankruptcy";
        public const string RunEndWin = "run_end_win";
        public const string RunEndBankrupt = "run_end_bankrupt";
        public const string TutorialHint = "tutorial_hint";
        public const string FallbackHint = "fallback_hint";
    }

    // Text keys, as written in ui_text.json.
    public static class UiKeys
    {
        public const string StationTitle = "station_title";
        public const string Credits = "credits";
        public const string Payment = "payment";
        public const string ShipsLeft = "ships_left";
        public const string DayOver = "day_over";
        public const string BayLabel = "bay_label";
        public const string BayEmpty = "bay_empty";
        public const string Needs = "needs";
        public const string Weakest = "weakest";
        public const string TypeFighter = "type_fighter";
        public const string TypeFreighter = "type_freighter";
        public const string PartEngine = "part_engine";
        public const string PartHullPlate = "part_hull_plate";
        public const string PartShieldGenerator = "part_shield_generator";
        public const string PartThruster = "part_thruster";
        public const string PartPowerCore = "part_power_core";
        public const string PartEmpty = "part_empty";
        public const string ReleaseButton = "release_button";
        public const string BuyButton = "buy_button";
        public const string InventoryTitle = "inventory_title";
        public const string InventoryFull = "inventory_full";
        public const string BinLabel = "bin_label";
        public const string BinHint = "bin_hint";
        public const string BinButton = "bin_button";
        public const string BuyTitle = "buy_title";
        public const string BuyPrice = "buy_price";
        public const string BuyBinned = "buy_binned";
        public const string ConfirmBuy = "confirm_buy";
        public const string CancelBuy = "cancel_buy";
        public const string ReleaseTitle = "release_title";
        public const string ReleasePayment = "release_payment";
        public const string ReleasePenalty = "release_penalty";
        public const string ReleaseNet = "release_net";
        public const string ConfirmRelease = "confirm_release";
        public const string CancelRelease = "cancel_release";

        // Key of a ship type's name.
        public static string ShipTypeKey(ShipType type)
        {
            return type == ShipType.Fighter ? TypeFighter : TypeFreighter;
        }

        // Key of a part type's name.
        public static string PartKey(PartType type)
        {
            switch (type)
            {
                case PartType.Engine: return PartEngine;
                case PartType.HullPlate: return PartHullPlate;
                case PartType.ShieldGenerator: return PartShieldGenerator;
                case PartType.Thruster: return PartThruster;
                case PartType.PowerCore: return PartPowerCore;
                default: return PartEngine;
            }
        }
    }

    // Loaded content with lookups. Any part may be null (file missing or broken):
    // lookups then return empty text and never throw.
    public class ContentData
    {
        private readonly MechanicLinesFile lines;
        private readonly DayBriefsFile briefs;
        private readonly PartCatalogFile parts;
        private readonly Random random;
        private readonly Dictionary<string, string> uiText = new Dictionary<string, string>();

        public ContentData(MechanicLinesFile lines, DayBriefsFile briefs, PartCatalogFile parts)
            : this(lines, briefs, parts, new Random(), null)
        {
        }

        public ContentData(MechanicLinesFile lines, DayBriefsFile briefs, PartCatalogFile parts, Random random)
            : this(lines, briefs, parts, random, null)
        {
        }

        public ContentData(MechanicLinesFile lines, DayBriefsFile briefs, PartCatalogFile parts, Random random,
            UiTextFile ui)
        {
            this.lines = lines;
            this.briefs = briefs;
            this.parts = parts;
            this.random = random ?? new Random();

            if (ui != null && ui.entries != null)
            {
                for (int i = 0; i < ui.entries.Length; i++)
                {
                    UiTextEntry e = ui.entries[i];
                    if (e == null || string.IsNullOrEmpty(e.key)) continue;
                    uiText[e.key] = e.text ?? "";
                }
            }
        }

        // Content with nothing loaded: every lookup falls back.
        public static ContentData Empty()
        {
            return new ContentData(null, null, null);
        }

        // UI text by key, with {0}, {1} filled from args. A missing key gives "[key]".
        // A placeholder with no matching argument stays as written.
        public string Text(string key, params object[] args)
        {
            string template;
            if (string.IsNullOrEmpty(key) || !uiText.TryGetValue(key, out template))
            {
                return "[" + (key ?? "") + "]";
            }
            return Fill(template, args);
        }

        // True when the key exists in the UI text.
        public bool HasText(string key)
        {
            return !string.IsNullOrEmpty(key) && uiText.ContainsKey(key);
        }

        // Fills {0}, {1}, ... in a template. A null argument gives empty text.
        // A number with no argument, or text that is not a number in braces, stays as it is.
        public static string Fill(string template, params object[] args)
        {
            if (string.IsNullOrEmpty(template)) return "";
            if (args == null || args.Length == 0) return template;

            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '{')
                {
                    int close = template.IndexOf('}', i + 1);
                    if (close > i + 1)
                    {
                        string num = template.Substring(i + 1, close - i - 1);
                        int index;
                        if (int.TryParse(num, NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
                            index < args.Length)
                        {
                            sb.Append(FormatArg(args[index]));
                            i = close + 1;
                            continue;
                        }
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string FormatArg(object arg)
        {
            if (arg == null) return "";
            IFormattable f = arg as IFormattable;
            if (f != null) return f.ToString(null, CultureInfo.InvariantCulture);
            return arg.ToString();
        }

        // Random line for a moment key. Returns "" if the key or the file is unknown.
        public string GetLine(string momentKey)
        {
            string[] pool = GetPool(momentKey);
            if (pool == null || pool.Length == 0) return "";
            string line = pool[random.Next(pool.Length)];
            return line ?? "";
        }

        // Number of lines stored for a moment key (0 if unknown).
        public int LineCount(string momentKey)
        {
            string[] pool = GetPool(momentKey);
            return pool == null ? 0 : pool.Length;
        }

        // Brief for a day number. Returns an empty brief (empty type and reason) if not found.
        public DayBrief GetBrief(int day)
        {
            if (briefs != null && briefs.days != null)
            {
                for (int i = 0; i < briefs.days.Length; i++)
                {
                    DayBrief b = briefs.days[i];
                    if (b != null && b.day == day)
                    {
                        return new DayBrief
                        {
                            day = b.day,
                            type = b.type ?? "",
                            reason = b.reason ?? ""
                        };
                    }
                }
            }
            return new DayBrief { day = day, type = "", reason = "" };
        }

        // Name and description for a part type (e.g. "engine"). Empty text if not found.
        public PartInfo GetPartInfo(string partType)
        {
            if (parts != null && parts.parts != null && partType != null)
            {
                for (int i = 0; i < parts.parts.Length; i++)
                {
                    PartInfo p = parts.parts[i];
                    if (p != null && p.type == partType)
                    {
                        return new PartInfo
                        {
                            type = p.type,
                            name = p.name ?? "",
                            description = p.description ?? ""
                        };
                    }
                }
            }
            return new PartInfo { type = partType ?? "", name = "", description = "" };
        }

        private string[] GetPool(string momentKey)
        {
            if (lines == null || lines.moments == null || momentKey == null) return null;
            MomentLines m = lines.moments;
            switch (momentKey)
            {
                case MomentKeys.DayStart: return m.day_start;
                case MomentKeys.ReleaseWarning: return m.release_warning;
                case MomentKeys.BuyWarning: return m.buy_warning;
                case MomentKeys.PenaltyFill: return m.penalty_fill;
                case MomentKeys.DayEnd: return m.day_end;
                case MomentKeys.Bankruptcy: return m.bankruptcy;
                case MomentKeys.RunEndWin: return m.run_end_win;
                case MomentKeys.RunEndBankrupt: return m.run_end_bankrupt;
                case MomentKeys.TutorialHint: return m.tutorial_hint;
                case MomentKeys.FallbackHint: return m.fallback_hint;
                default: return null;
            }
        }
    }
}
