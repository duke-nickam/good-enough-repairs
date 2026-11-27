using System;

namespace GoodEnoughRepairs
{
    /// <summary>Where the run is in its day flow.</summary>
    public enum RunPhase
    {
        /// <summary>Waiting for the start-the-day command.</summary>
        BeforeDay,
        /// <summary>A day is being played.</summary>
        InDay,
        /// <summary>The day ended and its summary is shown; waiting for continue.</summary>
        DayEnded,
        /// <summary>The run is over.</summary>
        Over
    }

    /// <summary>How a finished run ended.</summary>
    public enum RunOutcome
    {
        None,
        Won,
        Bankrupt
    }

    /// <summary>Why a run command was refused.</summary>
    public enum RunRefusal
    {
        None,
        RunOver,
        DayInProgress,
        SummaryPending,
        NoDayEnded
    }

    /// <summary>Result of start-the-day or continue. Refusals never throw.</summary>
    public sealed class RunResult
    {
        public bool Success { get; private set; }
        public RunRefusal Reason { get; private set; }

        private RunResult(bool success, RunRefusal reason)
        {
            Success = success;
            Reason = reason;
        }

        internal static RunResult Ok()
        {
            return new RunResult(true, RunRefusal.None);
        }

        internal static RunResult Refused(RunRefusal reason)
        {
            return new RunResult(false, reason);
        }
    }

    /// <summary>
    /// The whole game as code. Holds credits, the bays, the inventory and the day flow.
    /// Every day is a quiet day with a fixed ship count from balance.json: ships arrive
    /// one by one (up to 3 docked), and the day ends when the last one is released.
    /// Upkeep is charged when the day starts. Credits may go below 0 during the day;
    /// bankruptcy is checked only at day end. The run ends on bankruptcy or after the
    /// last day. Events fire after the state has changed. Make a run with RunFactory.
    /// </summary>
    public sealed class Run
    {
        private readonly BalanceData balance;
        private readonly ContentData content;
        private readonly int seed;
        private readonly Random shipRng;
        private readonly DockingBays bays = new DockingBays();
        private readonly Inventory inventory = new Inventory();
        private readonly PartMoves moves;
        private readonly BuyRelease buyRelease;
        private readonly int lastDay;

        private RunPhase phase = RunPhase.BeforeDay;
        private RunOutcome outcome = RunOutcome.None;
        private int credits;
        private int day = 1;
        private DayBalance dayConfig;
        private int shipsInDay;
        private int shipsCreated;
        private int shipsReleased;
        private int baselineCredits;
        private int earned;
        private int spent;
        private int upkeepPaid;
        private int shipsRepaired;
        private int totalEarned;
        private DaySummary lastSummary;

        /// <summary>Credits changed: the new total and the change.</summary>
        public event Action<int, int> CreditsChanged;

        /// <summary>A day started (upkeep already charged): the day number and its ship count.</summary>
        public event Action<int, int> DayStarted;

        /// <summary>A day ended, with its summary.</summary>
        public event Action<DaySummary> DayEnded;

        /// <summary>The balance was below 0 at day end.</summary>
        public event Action<DaySummary> Bankrupt;

        /// <summary>The run ended, with how.</summary>
        public event Action<RunOutcome> RunEnded;

        internal Run(BalanceData balance, ContentData content, int seed)
        {
            if (balance == null) throw new ArgumentNullException("balance");
            this.balance = balance;
            this.content = content ?? ContentData.Empty();
            this.seed = seed;

            // Derived streams: one for ships (a junkyard stream joins later).
            Random master = new Random(seed);
            shipRng = new Random(master.Next());

            moves = new PartMoves(inventory, balance);
            buyRelease = new BuyRelease(bays, inventory, balance);
            moves.PartBinned += OnBinned;
            buyRelease.PartBought += OnBought;
            buyRelease.ShipReleased += OnReleased;

            lastDay = GameRules.RunDays;
            credits = balance.startingCredits;
            baselineCredits = credits;
            dayConfig = GetDayBalance(day);
        }

        // ---- reading state ----

        public BalanceData Balance { get { return balance; } }
        public ContentData Content { get { return content; } }
        public int Seed { get { return seed; } }
        public int Credits { get { return credits; } }
        /// <summary>The current day, 1 to 7. During the day-ended phase it is the day that ended.</summary>
        public int Day { get { return day; } }
        public RunPhase Phase { get { return phase; } }
        public RunOutcome Outcome { get { return outcome; } }
        public bool IsOver { get { return phase == RunPhase.Over; } }
        public DockingBays Bays { get { return bays; } }
        public Inventory Inventory { get { return inventory; } }
        /// <summary>
        /// The run's own move and bin commands. Views bind to these, so every
        /// credit change goes through the run.
        /// </summary>
        public PartMoves Moves { get { return moves; } }
        /// <summary>
        /// The run's own buy and release commands (with previews). Views bind to
        /// these, so every credit change goes through the run.
        /// </summary>
        public BuyRelease Commands { get { return buyRelease; } }
        /// <summary>The last finished day's summary, or null.</summary>
        public DaySummary LastSummary { get { return lastSummary; } }
        /// <summary>Ships released without any penalty fill, over the run.</summary>
        public int ShipsRepaired { get { return shipsRepaired; } }
        /// <summary>Credits earned over the run (payments and scrap).</summary>
        public int TotalEarned { get { return totalEarned; } }
        /// <summary>Ships in today's fixed count.</summary>
        public int ShipsInDay { get { return shipsInDay; } }
        /// <summary>Ships of today not yet released (docked or still to come).</summary>
        public int ShipsLeftInDay
        {
            get { return phase == RunPhase.InDay ? shipsInDay - shipsReleased : 0; }
        }

        // ---- day commands ----

        /// <summary>Starts the day: charges upkeep, then the first ships dock.</summary>
        public RunResult StartDay()
        {
            if (phase == RunPhase.Over) return RunResult.Refused(RunRefusal.RunOver);
            if (phase == RunPhase.InDay) return RunResult.Refused(RunRefusal.DayInProgress);
            if (phase == RunPhase.DayEnded) return RunResult.Refused(RunRefusal.SummaryPending);

            dayConfig = GetDayBalance(day);
            shipsInDay = dayConfig.ships < 0 ? 0 : dayConfig.ships;
            shipsCreated = 0;
            shipsReleased = 0;
            phase = RunPhase.InDay;

            upkeepPaid = balance.upkeep;
            Change(-upkeepPaid);

            Action<int, int> handler = DayStarted;
            if (handler != null) handler(day, shipsInDay);

            while (shipsCreated < shipsInDay && !bays.IsFull) DockNext();
            if (shipsInDay == 0) EndDay();
            return RunResult.Ok();
        }

        /// <summary>After a day summary: moves on to the next day.</summary>
        public RunResult Continue()
        {
            if (phase == RunPhase.Over) return RunResult.Refused(RunRefusal.RunOver);
            if (phase != RunPhase.DayEnded) return RunResult.Refused(RunRefusal.NoDayEnded);
            day++;
            phase = RunPhase.BeforeDay;
            return RunResult.Ok();
        }

        // ---- player commands (pass through; credits follow from the events) ----

        public MoveResult Move(PartSlot from, PartSlot to)
        {
            return moves.Move(from, to);
        }

        public BinResult Bin(PartSlot slot)
        {
            return moves.Bin(slot);
        }

        public BuyPreview PreviewBuy(Ship ship, PartType type)
        {
            return buyRelease.PreviewBuy(ship, type);
        }

        public BuyResult Buy(Ship ship, PartType type)
        {
            return buyRelease.Buy(ship, type);
        }

        public ReleasePreview PreviewRelease(Ship ship)
        {
            return buyRelease.PreviewRelease(ship);
        }

        public ReleaseResult Release(Ship ship)
        {
            return buyRelease.Release(ship);
        }

        // ---- internals ----

        private DayBalance GetDayBalance(int number)
        {
            DayBalance[] list = balance.days;
            if (list == null || list.Length == 0) return new DayBalance { day = number, type = "quiet" };
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] != null && list[i].day == number) return list[i];
            }
            int index = number - 1;
            if (index < 0) index = 0;
            if (index >= list.Length) index = list.Length - 1;
            return list[index] ?? new DayBalance { day = number, type = "quiet" };
        }

        private void Change(int delta)
        {
            if (delta == 0) return;
            credits += delta;
            Action<int, int> handler = CreditsChanged;
            if (handler != null) handler(credits, delta);
        }

        private void Earn(int amount)
        {
            earned += amount;
            totalEarned += amount;
        }

        private void OnBinned(BinResult r)
        {
            Earn(r.ScrapAmount);
            Change(r.ScrapAmount);
        }

        private void OnBought(BuyResult r)
        {
            spent += r.Price;
            int scrap = r.OldPartBinned ? r.ScrapAmount : 0;
            Earn(scrap);
            Change(scrap - r.Price);
        }

        private void OnReleased(ReleaseResult r)
        {
            Earn(r.Payment);
            spent += r.PenaltyTotal;
            if (r.PenaltyFills == 0) shipsRepaired++;
            Change(r.NetResult);

            if (phase != RunPhase.InDay) return;
            shipsReleased++;
            if (shipsCreated < shipsInDay) DockNext();
            if (shipsReleased >= shipsInDay) EndDay();
        }

        private void DockNext()
        {
            Ship ship = ShipFactory.Create(balance, dayConfig, shipRng);
            shipsCreated++;
            bays.TryDock(ship);
        }

        private void EndDay()
        {
            bool isLast = day >= lastDay;
            bool isBankrupt = credits < 0;
            DaySummary summary = new DaySummary(day, baselineCredits, earned, spent, upkeepPaid, credits,
                shipsReleased, isBankrupt, isLast);
            lastSummary = summary;
            baselineCredits = credits;
            earned = 0;
            spent = 0;
            upkeepPaid = 0;

            bool over = isBankrupt || isLast;
            phase = over ? RunPhase.Over : RunPhase.DayEnded;
            if (isBankrupt) outcome = RunOutcome.Bankrupt;
            else if (isLast) outcome = RunOutcome.Won;

            Action<DaySummary> ended = DayEnded;
            if (ended != null) ended(summary);

            if (isBankrupt)
            {
                Action<DaySummary> b = Bankrupt;
                if (b != null) b(summary);
            }
            if (over)
            {
                Action<RunOutcome> r = RunEnded;
                if (r != null) r(outcome);
            }
        }
    }

    /// <summary>
    /// What the credits counter shows, as plain data. It follows the run's credits
    /// changed event. The credits view only displays Shown.
    /// </summary>
    public sealed class CreditsDisplay
    {
        private Run run;

        /// <summary>The credits shown now.</summary>
        public int Shown { get; private set; }

        /// <summary>Fires after Shown changed (also when bound).</summary>
        public event Action<int> Changed;

        /// <summary>Connects to a run (or a new run) and takes its credits.</summary>
        public void Bind(Run newRun)
        {
            Unbind();
            run = newRun;
            if (run == null) return;
            run.CreditsChanged += OnCreditsChanged;
            Set(run.Credits);
        }

        /// <summary>Disconnects from the run.</summary>
        public void Unbind()
        {
            if (run != null) run.CreditsChanged -= OnCreditsChanged;
            run = null;
        }

        private void OnCreditsChanged(int total, int delta)
        {
            Set(total);
        }

        private void Set(int total)
        {
            Shown = total;
            Action<int> handler = Changed;
            if (handler != null) handler(total);
        }
    }

    /// <summary>
    /// What the end-of-day panel shows, as plain data: open or closed, the title
    /// key, which button is offered. The summary view only displays it and
    /// forwards its buttons. It calls the run's commands and holds no other rules.
    /// </summary>
    public sealed class SummaryPanelState
    {
        public const string TitleDay = "summary_title";
        public const string TitleWin = "run_end_title_win";
        public const string TitleBankrupt = "run_end_title_bankrupt";

        private Run run;
        private Action onContinued;
        private Action onPlayAgain;

        public bool IsOpen { get; private set; }
        public bool ShowsContinue { get; private set; }
        public bool ShowsPlayAgain { get; private set; }
        public bool IsRunOver { get; private set; }
        public string TitleKey { get; private set; }
        public DaySummary Shown { get; private set; }

        /// <summary>Fires after the state changed.</summary>
        public event Action Changed;

        /// <summary>
        /// Connects to a run and closes the panel. continued is called after
        /// Continue moved the run on; playAgain when Play again is pressed.
        /// </summary>
        public void Bind(Run newRun, Action continued, Action playAgain)
        {
            Unbind();
            run = newRun;
            onContinued = continued;
            onPlayAgain = playAgain;
            if (run != null) run.DayEnded += OnDayEnded;
            Hide();
        }

        /// <summary>Disconnects from the run.</summary>
        public void Unbind()
        {
            if (run != null) run.DayEnded -= OnDayEnded;
            run = null;
        }

        private void OnDayEnded(DaySummary summary)
        {
            Shown = summary;
            IsOpen = true;
            IsRunOver = summary.IsBankrupt || summary.IsLastDay;
            ShowsContinue = !IsRunOver;
            ShowsPlayAgain = IsRunOver;
            if (summary.IsBankrupt) TitleKey = TitleBankrupt;
            else if (summary.IsLastDay) TitleKey = TitleWin;
            else TitleKey = TitleDay;
            Raise();
        }

        private void Hide()
        {
            IsOpen = false;
            ShowsContinue = false;
            ShowsPlayAgain = false;
            IsRunOver = false;
            Shown = null;
            Raise();
        }

        private void Raise()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>Continue: asks the run to move on, then closes. False when refused.</summary>
        public bool PressContinue()
        {
            if (!IsOpen || !ShowsContinue || run == null) return false;
            RunResult result = run.Continue();
            if (!result.Success) return false;
            Hide();
            if (onContinued != null) onContinued();
            return true;
        }

        /// <summary>Play again: closes and asks for a new run. False when not offered.</summary>
        public bool PressPlayAgain()
        {
            if (!IsOpen || !ShowsPlayAgain) return false;
            Hide();
            if (onPlayAgain != null) onPlayAgain();
            return true;
        }
    }
}
