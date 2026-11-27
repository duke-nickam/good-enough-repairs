using System;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// Makes a run from balance data, content and a seed. The same three give the
    /// same run. The game start, tests and bots all use this.
    /// </summary>
    public static class RunFactory
    {
        public static Run Create(BalanceData balance, ContentData content, int seed)
        {
            if (balance == null) throw new ArgumentNullException("balance");
            return new Run(balance, content ?? ContentData.Empty(), seed);
        }
    }
}
