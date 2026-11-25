using System;
using UnityEngine;

namespace GoodEnoughRepairs
{
    // Reads the content text from the game data asset at game start.
    // Never throws: on a missing or bad file it logs an error and that part
    // of the content falls back to empty text.
    public static class ContentLoader
    {
        // The names of the data files, used in messages.
        public const string LinesFileName = "mechanic_lines.json";
        public const string BriefsFileName = "day_briefs.json";
        public const string PartsFileName = "part_catalog.json";
        public const string UiTextFileName = "ui_text.json";

        // For callers that were not given the asset: uses the most recently
        // loaded game data asset. Without one, returns empty content.
        public static ContentData Load()
        {
            return Load(GameDataAsset.Current);
        }

        // Always returns a usable ContentData, never null.
        public static ContentData Load(GameDataAsset asset)
        {
            if (asset == null)
            {
                Debug.LogError("[Content] No game data asset is assigned.");
            }

            MechanicLinesFile lines = LoadFile<MechanicLinesFile>(asset != null ? asset.mechanicLines : null, LinesFileName, ParseLines);
            DayBriefsFile briefs = LoadFile<DayBriefsFile>(asset != null ? asset.dayBriefs : null, BriefsFileName, ParseBriefs);
            PartCatalogFile parts = LoadFile<PartCatalogFile>(asset != null ? asset.partCatalog : null, PartsFileName, ParseParts);
            UiTextFile ui = LoadFile<UiTextFile>(asset != null ? asset.uiText : null, UiTextFileName, ParseUiText);
            return new ContentData(lines, briefs, parts, null, ui);
        }

        private delegate T ParseFunc<T>(string json, out string error) where T : class;

        private static T LoadFile<T>(TextAsset text, string fileName, ParseFunc<T> parse) where T : class
        {
            if (text == null)
            {
                Debug.LogError("[Content] No text asset assigned for " + fileName);
                return null;
            }

            string json;
            try
            {
                json = text.text;
            }
            catch (Exception e)
            {
                Debug.LogError("[Content] Could not read " + fileName + ": " + e.Message);
                return null;
            }

            string error;
            T data = parse(json, out error);
            if (data == null)
            {
                Debug.LogError("[Content] " + fileName + " is not usable: " + error);
            }
            return data;
        }

        // Parse methods return null and set error on failure. They do not log.

        public static MechanicLinesFile ParseLines(string json, out string error)
        {
            MechanicLinesFile data = ParseJson<MechanicLinesFile>(json, out error);
            if (data == null) return null;
            if (data.moments == null) { error = "missing key: moments"; return null; }
            return data;
        }

        public static DayBriefsFile ParseBriefs(string json, out string error)
        {
            DayBriefsFile data = ParseJson<DayBriefsFile>(json, out error);
            if (data == null) return null;
            if (data.days == null || data.days.Length == 0) { error = "missing or empty key: days"; return null; }
            return data;
        }

        public static PartCatalogFile ParseParts(string json, out string error)
        {
            PartCatalogFile data = ParseJson<PartCatalogFile>(json, out error);
            if (data == null) return null;
            if (data.parts == null || data.parts.Length == 0) { error = "missing or empty key: parts"; return null; }
            return data;
        }

        public static UiTextFile ParseUiText(string json, out string error)
        {
            UiTextFile data = ParseJson<UiTextFile>(json, out error);
            if (data == null) return null;
            if (data.entries == null || data.entries.Length == 0) { error = "missing or empty key: entries"; return null; }
            return data;
        }

        private static T ParseJson<T>(string json, out string error) where T : class
        {
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "file is empty";
                return null;
            }

            T data;
            try
            {
                data = JsonUtility.FromJson<T>(json);
            }
            catch (Exception e)
            {
                error = "malformed JSON (" + e.Message + ")";
                return null;
            }

            if (data == null)
            {
                error = "malformed JSON (nothing was read)";
                return null;
            }

            return data;
        }
    }
}
