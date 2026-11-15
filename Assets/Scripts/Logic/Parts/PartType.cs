using System;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// The five part types from the GDD. Fighters and freighters use the same
    /// types, so a part fits the matching slot on either ship.
    /// </summary>
    public enum PartType
    {
        Engine,
        HullPlate,
        ShieldGenerator,
        Thruster,
        PowerCore
    }

    public static class PartTypes
    {
        private static readonly PartType[] all =
        {
            PartType.Engine,
            PartType.HullPlate,
            PartType.ShieldGenerator,
            PartType.Thruster,
            PartType.PowerCore
        };

        /// <summary>The five types in GDD order. A copy, so callers cannot change it.</summary>
        public static PartType[] All
        {
            get { return (PartType[])all.Clone(); }
        }

        /// <summary>Number of part types.</summary>
        public static int Count
        {
            get { return all.Length; }
        }

        /// <summary>Key used in part_catalog.json, for example "hull_plate".</summary>
        public static string Key(PartType type)
        {
            switch (type)
            {
                case PartType.Engine: return "engine";
                case PartType.HullPlate: return "hull_plate";
                case PartType.ShieldGenerator: return "shield_generator";
                case PartType.Thruster: return "thruster";
                case PartType.PowerCore: return "power_core";
                default: throw new ArgumentOutOfRangeException("type");
            }
        }
    }
}
