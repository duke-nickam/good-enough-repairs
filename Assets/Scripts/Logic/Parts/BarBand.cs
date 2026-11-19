using System;

namespace GoodEnoughRepairs
{
    /// <summary>The band of a condition bar: red, green or blue.</summary>
    public enum BarBandKind
    {
        /// <summary>Empty slot or below the ship's required level.</summary>
        Red,

        /// <summary>Passes the required level.</summary>
        Green,

        /// <summary>Far enough above the required level to be worth harvesting.</summary>
        Blue
    }

    /// <summary>
    /// The one place that decides a bar's band. Pure: no UnityEngine, no state.
    /// Views only map a band to a color.
    /// </summary>
    public static class BarBand
    {
        /// <summary>
        /// Band of a condition against the ship's required level. Below the level is
        /// red. At or above the level plus surplusMargin is blue. Anything else that
        /// passes is green.
        /// </summary>
        public static BarBandKind Of(int condition, int requiredLevel, int surplusMargin)
        {
            if (condition < requiredLevel) return BarBandKind.Red;
            if (condition >= requiredLevel + surplusMargin) return BarBandKind.Blue;
            return BarBandKind.Green;
        }

        /// <summary>Band of an installed part. An empty slot (null) is red.</summary>
        public static BarBandKind Of(Part part, int requiredLevel, int surplusMargin)
        {
            if (part == null) return BarBandKind.Red;
            return Of(part.Condition, requiredLevel, surplusMargin);
        }

        /// <summary>
        /// How far above the required level a part must be to count as surplus:
        /// the fighter level minus the freighter level, never below 0. A part that
        /// passes the fighter level is then surplus on a freighter.
        /// </summary>
        public static int SurplusMargin(int fighterLevel, int freighterLevel)
        {
            return Math.Max(0, fighterLevel - freighterLevel);
        }
    }
}
