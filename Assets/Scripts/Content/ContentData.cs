using System;

namespace GoodEnoughRepairs
{
    // Mirrors the three JSON files in Assets/StreamingAssets/content key for key.
    // Plain data only: no Unity types, no game rules.

    // mechanic_lines.json: "moments" is an object with one array per moment key.
    // JsonUtility cannot read dictionaries, so every key is a field.
    [Serializable]
    public class MomentLines
    {
        public string[] day_start;
        public string[] release_warning;
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

    // Moment keys, as written in mechanic_lines.json.
    public static class MomentKeys
    {
        public const string DayStart = "day_start";
        public const string ReleaseWarning = "release_warning";
        public const string PenaltyFill = "penalty_fill";
        public const string DayEnd = "day_end";
        public const string Bankruptcy = "bankruptcy";
        public const string RunEndWin = "run_end_win";
        public const string RunEndBankrupt = "run_end_bankrupt";
        public const string TutorialHint = "tutorial_hint";
        public const string FallbackHint = "fallback_hint";
    }

    // Loaded content with lookups. Any part may be null (file missing or broken):
    // lookups then return empty text and never throw.
    public class ContentData
    {
        private readonly MechanicLinesFile lines;
        private readonly DayBriefsFile briefs;
        private readonly PartCatalogFile parts;
        private readonly Random random;

        public ContentData(MechanicLinesFile lines, DayBriefsFile briefs, PartCatalogFile parts)
            : this(lines, briefs, parts, new Random())
        {
        }

        public ContentData(MechanicLinesFile lines, DayBriefsFile briefs, PartCatalogFile parts, Random random)
        {
            this.lines = lines;
            this.briefs = briefs;
            this.parts = parts;
            this.random = random ?? new Random();
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
