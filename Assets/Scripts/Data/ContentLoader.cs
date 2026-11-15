using System;
using System.IO;
using UnityEngine;

namespace GoodEnoughRepairs
{
    // Reads the content JSON files from StreamingAssets/content at game start.
    // Never throws: on a missing or bad file it logs an error and that part
    // of the content falls back to empty text.
    public static class ContentLoader
    {
        public const string FolderName = "content";
        public const string LinesFileName = "mechanic_lines.json";
        public const string BriefsFileName = "day_briefs.json";
        public const string PartsFileName = "part_catalog.json";

        // Always returns a usable ContentData, never null.
        public static ContentData Load()
        {
            MechanicLinesFile lines = LoadFile<MechanicLinesFile>(LinesFileName, ParseLines);
            DayBriefsFile briefs = LoadFile<DayBriefsFile>(BriefsFileName, ParseBriefs);
            PartCatalogFile parts = LoadFile<PartCatalogFile>(PartsFileName, ParseParts);
            return new ContentData(lines, briefs, parts);
        }

        private delegate T ParseFunc<T>(string json, out string error) where T : class;

        private static T LoadFile<T>(string fileName, ParseFunc<T> parse) where T : class
        {
            string path = Path.Combine(Path.Combine(Application.streamingAssetsPath, FolderName), fileName);

            if (!File.Exists(path))
            {
                Debug.LogError("[Content] File not found: " + path);
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Debug.LogError("[Content] Could not read " + path + ": " + e.Message);
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
