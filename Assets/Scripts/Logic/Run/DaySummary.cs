namespace GoodEnoughRepairs
{
    /// <summary>
    /// What the end-of-day screen shows. Immutable.
    /// StartCredits + Earned - Spent - Upkeep = Balance.
    /// Earned is ship payments plus scrap from binned parts. Spent is bought parts,
    /// penalty fills and salvage. Upkeep is its own line. Balance is the credits at day end.
    /// </summary>
    public sealed class DaySummary
    {
        public int Day { get; private set; }
        /// <summary>Credits at the end of the previous day (the starting credits on day 1).</summary>
        public int StartCredits { get; private set; }
        public int Earned { get; private set; }
        public int Spent { get; private set; }
        public int Upkeep { get; private set; }
        public int Balance { get; private set; }
        public int ShipsReleased { get; private set; }
        /// <summary>True when the balance is below 0: the run ends as bankrupt.</summary>
        public bool IsBankrupt { get; private set; }
        /// <summary>True when this is the last day of the run.</summary>
        public bool IsLastDay { get; private set; }

        internal DaySummary(int day, int startCredits, int earned, int spent, int upkeep, int balance,
            int shipsReleased, bool bankrupt, bool lastDay)
        {
            Day = day;
            StartCredits = startCredits;
            Earned = earned;
            Spent = spent;
            Upkeep = upkeep;
            Balance = balance;
            ShipsReleased = shipsReleased;
            IsBankrupt = bankrupt;
            IsLastDay = lastDay;
        }
    }
}
