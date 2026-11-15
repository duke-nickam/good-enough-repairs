using System;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// One ship in a bay: type, payment, required level and five part slots,
    /// one per part type. Read-only for callers. The factory fills the slots;
    /// later game code in this assembly changes them through the internal setter.
    /// </summary>
    public sealed class Ship
    {
        private readonly Part[] parts = new Part[GameRules.PartSlotsPerShip];

        public ShipType Type { get; private set; }
        public int Payment { get; private set; }
        public int RequiredLevel { get; private set; }

        internal Ship(ShipType type, int payment, int requiredLevel)
        {
            Type = type;
            Payment = payment;
            RequiredLevel = requiredLevel;
        }

        /// <summary>The part in the slot of this type, or null if the slot is empty.</summary>
        public Part GetPart(PartType type)
        {
            return parts[(int)type];
        }

        /// <summary>The slots in PartTypes.All order. A copy; empty slots are null.</summary>
        public Part[] Parts
        {
            get { return (Part[])parts.Clone(); }
        }

        /// <summary>
        /// The installed part with the lowest condition (the first one on a tie),
        /// or null if every slot is empty.
        /// </summary>
        public Part WeakestPart
        {
            get
            {
                Part weakest = null;
                for (int i = 0; i < parts.Length; i++)
                {
                    Part p = parts[i];
                    if (p == null) continue;
                    if (weakest == null || p.Condition < weakest.Condition) weakest = p;
                }
                return weakest;
            }
        }

        /// <summary>Slots that are empty or below the required level.</summary>
        public int BelowLevelCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == null || !parts[i].Passes(RequiredLevel)) n++;
                }
                return n;
            }
        }

        /// <summary>True when every slot holds a part at or above the required level.</summary>
        public bool IsRepaired
        {
            get { return BelowLevelCount == 0; }
        }

        internal void SetPart(PartType type, Part part)
        {
            parts[(int)type] = part;
        }
    }
}
