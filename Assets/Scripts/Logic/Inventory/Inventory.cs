namespace GoodEnoughRepairs
{
    /// <summary>
    /// The shared storage for removed parts: GameRules.InventorySlots slots, used by
    /// all three bays. Slots keep their place: a removed part leaves a gap and the
    /// other parts do not shift. Moving and binning go through PartMoves.
    /// </summary>
    public sealed class Inventory
    {
        private readonly Part[] slots = new Part[GameRules.InventorySlots];

        public int SlotCount
        {
            get { return slots.Length; }
        }

        public int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < slots.Length; i++) if (slots[i] != null) n++;
                return n;
            }
        }

        public bool IsFull
        {
            get { return Count == slots.Length; }
        }

        /// <summary>The part in a slot, or null if the slot is empty or the index is invalid.</summary>
        public Part GetPart(int slot)
        {
            if (slot < 0 || slot >= slots.Length) return null;
            return slots[slot];
        }

        /// <summary>The slots in order. A copy; empty slots are null.</summary>
        public Part[] Parts
        {
            get { return (Part[])slots.Clone(); }
        }

        /// <summary>The first empty slot, or -1 when the inventory is full.</summary>
        public int FirstFreeSlot
        {
            get
            {
                for (int i = 0; i < slots.Length; i++) if (slots[i] == null) return i;
                return -1;
            }
        }

        /// <summary>
        /// Puts a part into the first free slot (for salvage and buying later).
        /// Returns false, and changes nothing, when the part is null or the inventory is full.
        /// </summary>
        public bool TryAdd(Part part, out int slot)
        {
            slot = -1;
            if (part == null) return false;
            int free = FirstFreeSlot;
            if (free < 0) return false;
            slots[free] = part;
            slot = free;
            return true;
        }

        internal void SetPart(int slot, Part part)
        {
            if (slot < 0 || slot >= slots.Length) return;
            slots[slot] = part;
        }
    }
}
