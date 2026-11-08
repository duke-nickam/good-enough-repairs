using System;
using System.IO;
using UnityEngine;

namespace GoodEnoughRepairs
{
    // Reads balance.json from StreamingAssets at game start.
    // Never throws: on a missing or bad file it logs an error and returns null.
    public static class BalanceLoader
    {
        public const string FileName = "balance.json";

        public static BalanceData Load()
        {
            string path = Path.Combine(Application.streamingAssetsPath, FileName);

            if (!File.Exists(path))
            {
                Debug.LogError("[Balance] File not found: " + path);
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Debug.LogError("[Balance] Could not read " + path + ": " + e.Message);
                return null;
            }

            string error;
            BalanceData data = Parse(json, out error);
            if (data == null)
            {
                Debug.LogError("[Balance] " + FileName + " is not usable: " + error);
            }
            return data;
        }

        // Parses JSON text. Returns null and sets error if it fails. Does not log.
        public static BalanceData Parse(string json, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "file is empty";
                return null;
            }

            BalanceData data;
            try
            {
                data = JsonUtility.FromJson<BalanceData>(json);
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

            if (data.freighterPayment == null) { error = "missing key: freighterPayment"; return null; }
            if (data.fighterPayment == null) { error = "missing key: fighterPayment"; return null; }
            if (data.junkyard == null) { error = "missing key: junkyard"; return null; }
            if (data.days == null || data.days.Length == 0) { error = "missing or empty key: days"; return null; }

            return data;
        }
    }
}
