namespace GoodEnoughRepairs
{
    /// <summary>Why a move or bin command was refused.</summary>
    public enum MoveRefusal
    {
        None,
        /// <summary>A slot is missing, or its ship or inventory index is invalid.</summary>
        InvalidSlot,
        /// <summary>The part was dropped on the slot it came from.</summary>
        SameSlot,
        /// <summary>There is no part in the source slot.</summary>
        SourceEmpty,
        /// <summary>A part would go into a ship slot of another part type.</summary>
        WrongSlotType,
        /// <summary>The inventory is full, so the part snaps back.</summary>
        InventoryFull
    }

    /// <summary>Result of a move command. Refusals never throw and change nothing.</summary>
    public sealed class MoveResult
    {
        public bool Success { get; private set; }
        public MoveRefusal Reason { get; private set; }
        /// <summary>The part that was dragged.</summary>
        public Part Part { get; private set; }
        /// <summary>The part that went the other way in a swap, or null.</summary>
        public Part SwappedPart { get; private set; }
        public PartSlot From { get; private set; }
        /// <summary>The slot the part landed in (an inventory slot has its real index).</summary>
        public PartSlot To { get; private set; }

        public bool IsSwap
        {
            get { return Success && SwappedPart != null; }
        }

        private MoveResult(bool success, MoveRefusal reason, Part part, Part swapped, PartSlot from, PartSlot to)
        {
            Success = success;
            Reason = reason;
            Part = part;
            SwappedPart = swapped;
            From = from;
            To = to;
        }

        internal static MoveResult Ok(Part part, Part swapped, PartSlot from, PartSlot to)
        {
            return new MoveResult(true, MoveRefusal.None, part, swapped, from, to);
        }

        internal static MoveResult Refused(MoveRefusal reason)
        {
            return new MoveResult(false, reason, null, null, null, null);
        }
    }

    /// <summary>Result of a bin command. Credits are added later by the economy.</summary>
    public sealed class BinResult
    {
        public bool Success { get; private set; }
        public MoveRefusal Reason { get; private set; }
        /// <summary>The binned part.</summary>
        public Part Part { get; private set; }
        public PartSlot From { get; private set; }
        /// <summary>The scrap payment from balance.json. 0 when refused.</summary>
        public int ScrapAmount { get; private set; }

        private BinResult(bool success, MoveRefusal reason, Part part, PartSlot from, int scrap)
        {
            Success = success;
            Reason = reason;
            Part = part;
            From = from;
            ScrapAmount = scrap;
        }

        internal static BinResult Ok(Part part, PartSlot from, int scrap)
        {
            return new BinResult(true, MoveRefusal.None, part, from, scrap);
        }

        internal static BinResult Refused(MoveRefusal reason)
        {
            return new BinResult(false, reason, null, null, 0);
        }
    }
}
