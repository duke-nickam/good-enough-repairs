using System;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// Creates a ship just before it docks. Holds no state: the caller owns the
    /// System.Random stream (the run's ship stream).
    /// Draw order, fixed so a seed gives the same ships:
    /// type, payment, weak count, weak slot shuffle, then one condition per slot
    /// in PartTypes.All order.
    /// </summary>
    public static class ShipFactory
    {
        public static Ship Create(BalanceData balance, DayBalance day, Random rng)
        {
            // 1. Type (50/50, designer-approved).
            ShipType type = rng.Next(2) == 0 ? ShipType.Fighter : ShipType.Freighter;
            bool fighter = type == ShipType.Fighter;

            int level = fighter ? balance.requiredFighterPercent : balance.requiredFreighterPercent;
            PaymentRange range = fighter ? balance.fighterPayment : balance.freighterPayment;
            int weakMin = fighter ? day.fighterWeakConditionMin : day.freighterWeakConditionMin;
            int weakMax = fighter ? day.fighterWeakConditionMax : day.freighterWeakConditionMax;

            // 2. Payment.
            int payment = DrawRange(rng, range.min, range.max);

            // 3. Weak count, always 1 to the slot count.
            int weakCount = DrawRange(rng, day.weakPartsMin, day.weakPartsMax);
            if (weakCount < 1) weakCount = 1;
            if (weakCount > GameRules.PartSlotsPerShip) weakCount = GameRules.PartSlotsPerShip;

            // 4. Weak slots: partial shuffle, the first weakCount are weak.
            PartType[] order = PartTypes.All;
            for (int i = 0; i < weakCount; i++)
            {
                int j = rng.Next(i, order.Length);
                PartType t = order[i];
                order[i] = order[j];
                order[j] = t;
            }
            bool[] weak = new bool[order.Length];
            for (int i = 0; i < weakCount; i++) weak[(int)order[i]] = true;

            // 5. One part per slot, in slot order.
            Ship ship = new Ship(type, payment, level);
            PartType[] slots = PartTypes.All;
            for (int i = 0; i < slots.Length; i++)
            {
                PartType slot = slots[i];
                Part part;
                if (weak[(int)slot])
                {
                    part = PartGenerator.Create(rng, slot, weakMin, weakMax);
                    // Keep the rule "below the required level" even if the data breaks it.
                    if (part.Condition > level - 1) part = PartGenerator.Create(slot, level - 1);
                }
                else
                {
                    part = PartGenerator.Create(rng, slot, day.healthyConditionMin, day.healthyConditionMax);
                }
                ship.SetPart(slot, part);
            }
            return ship;
        }

        private static int DrawRange(Random rng, int min, int max)
        {
            if (min > max)
            {
                int t = min;
                min = max;
                max = t;
            }
            return rng.Next(min, max + 1);
        }
    }
}
