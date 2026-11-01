using System;
using UnityEngine;

namespace GoodEnoughRepairs.Data
{
    /// <summary>
    /// Economy values produced by the agent crew (agents/run.py) in Assets/Data/economy.json.
    /// Field names match the JSON keys, so JsonUtility can read the file directly.
    /// </summary>
    [Serializable]
    public class EconomyConfig
    {
        public string version;
        public int startingCredits;
        public int stationUpkeep;
        public int scrapPrice;
        public IntRange partPrice;
        public int penaltyPercent;
        public IntRange fighterPayment;
        public IntRange freighterPayment;
        public int junkyardPartPrice;
        public int junkyardParts;
        public IntRange junkyardJunk;
        public DayConfig[] days;

        public static EconomyConfig FromJson(TextAsset json) => JsonUtility.FromJson<EconomyConfig>(json.text);

        /// <summary>Price for filling a weak slot when a ship is released unfinished.</summary>
        public int PenaltyPrice(int partPrice) => Mathf.RoundToInt(partPrice * (1f + penaltyPercent / 100f));
    }

    [Serializable]
    public class IntRange
    {
        public int min;
        public int max;

        public int Roll(System.Random rng) => rng.Next(min, max + 1);
    }

    [Serializable]
    public class DayConfig
    {
        public int day;
        public string type; // "quiet" or "busy"
        public int ships;
        public int weakMin;
        public int weakMax;

        public bool IsBusy => type == "busy";
    }
}
