using System;
using UnityEngine;

namespace GoodEnoughRepairs
{
    // Reads the balance text from the game data asset at game start.
    // Never throws: on a missing or bad file it logs an error and returns null.
    public static class BalanceLoader
    {
        // The name of the data file, used in messages.
        public const string FileName = "balance.json";

        // For callers that were not given the asset (for example editor tools):
        // uses the most recently loaded game data asset, or returns null and logs.
        public static BalanceData Load()
        {
            return Load(GameDataAsset.Current);
        }

        public static BalanceData Load(GameDataAsset asset)
        {
            if (asset == null)
            {
                Debug.LogError("[Balance] No game data asset is assigned.");
                return null;
            }

            if (asset.balance == null)
            {
                Debug.LogError("[Balance] The game data asset has no " + FileName + " assigned.");
                return null;
            }

            string json;
            try
            {
                json = asset.balance.text;
            }
            catch (Exception e)
            {
                Debug.LogError("[Balance] Could not read " + FileName + ": " + e.Message);
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
