using System;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// Makes parts. Holds no state: the caller owns the System.Random stream.
    /// </summary>
    public static class PartGenerator
    {
        /// <summary>A part with an exact condition (clamped to 0-100).</summary>
        public static Part Create(PartType type, int condition)
        {
            return new Part(type, condition);
        }

        /// <summary>
        /// A part with a condition drawn from min-max (both included). The range is
        /// clamped to 0-100 and swapped if min is above max. One draw per part.
        /// </summary>
        public static Part Create(Random rng, PartType type, int conditionMin, int conditionMax)
        {
            int lo = Part.Clamp(conditionMin);
            int hi = Part.Clamp(conditionMax);
            if (lo > hi)
            {
                int t = lo;
                lo = hi;
                hi = t;
            }
            int condition = rng.Next(lo, hi + 1);
            return new Part(type, condition);
        }

        /// <summary>A part of a random type, then a random condition (two draws).</summary>
        public static Part CreateRandomType(Random rng, int conditionMin, int conditionMax)
        {
            PartType[] types = PartTypes.All;
            PartType type = types[rng.Next(types.Length)];
            return Create(rng, type, conditionMin, conditionMax);
        }
    }
}
