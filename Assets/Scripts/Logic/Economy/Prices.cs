using System;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// Price rules worked out from balance.json. The shop price is newPartPrice.
    /// The penalty price is the shop price plus penaltyPercent, rounded half up,
    /// the same as penalty_price() in agents/checks/simulate.py.
    /// </summary>
    public static class Prices
    {
        /// <summary>The shop price of a brand-new part.</summary>
        public static int NewPartPrice(BalanceData balance)
        {
            if (balance == null) throw new ArgumentNullException("balance");
            return balance.newPartPrice;
        }

        /// <summary>
        /// The price of a part that release fills in: shop price times
        /// (100 + penaltyPercent) / 100, rounded half up.
        /// </summary>
        public static int PenaltyPrice(BalanceData balance)
        {
            if (balance == null) throw new ArgumentNullException("balance");
            return (balance.newPartPrice * (100 + balance.penaltyPercent) + 50) / 100;
        }
    }
}
