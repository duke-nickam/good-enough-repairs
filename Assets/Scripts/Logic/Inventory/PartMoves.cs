using System;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// A place a part can be: a ship slot (ship plus part type) or an inventory slot.
    /// Immutable. Use the factory methods.
    /// </summary>
    public sealed class PartSlot
    {
        /// <summary>Index meaning "any free inventory slot" for a drop on the inventory panel.</summary>
        public const int AnyInventorySlot = -1;

        public Ship Ship { get; private set; }
        public PartType PartType { get; private set; }
        public int InventoryIndex { get; private set; }
        public bool IsInventory { get; private set; }

        private PartSlot(Ship ship, PartType type, int index, bool isInventory)
        {
            Ship = ship;
            PartType = type;
            InventoryIndex = index;
            IsInventory = isInventory;
        }

        public static PartSlot OnShip(Ship ship, PartType type)
        {
            return new PartSlot(ship, type, AnyInventorySlot, false);
        }

        public static PartSlot InInventory(int index)
        {
            return new PartSlot(null, default(PartType), index, true);
        }

        /// <summary>A drop on the inventory without a chosen slot: the first free slot is used.</summary>
        public static PartSlot InInventoryAny()
        {
            return new PartSlot(null, default(PartType), AnyInventorySlot, true);
        }
    }

    /// <summary>
    /// The player's part commands: move (with swap) and bin. Every command returns a
    /// result with a reason when refused, and never throws. Events fire after the
    /// state has changed. Credits for scrap are added later by the economy.
    /// </summary>
    public sealed class PartMoves
    {
        private readonly Inventory inventory;
        private readonly int scrapPrice;

        /// <summary>A part moved or two parts swapped.</summary>
        public event Action<MoveResult> PartMoved;

        /// <summary>A part was binned, with the scrap amount.</summary>
        public event Action<BinResult> PartBinned;

        public PartMoves(Inventory inventory, BalanceData balance)
        {
            if (inventory == null) throw new ArgumentNullException("inventory");
            if (balance == null) throw new ArgumentNullException("balance");
            this.inventory = inventory;
            scrapPrice = balance.scrapPrice;
        }

        public Inventory Inventory
        {
            get { return inventory; }
        }

        /// <summary>
        /// Moves the part in 'from' to 'to'. An occupied target swaps the two parts.
        /// A part only fits a ship slot of its own type. A plain move into a full
        /// inventory is refused and the part stays where it was.
        /// </summary>
        public MoveResult Move(PartSlot from, PartSlot to)
        {
            if (!IsValid(from) || !IsValid(to)) return MoveResult.Refused(MoveRefusal.InvalidSlot);

            if (from.IsInventory && from.InventoryIndex == PartSlot.AnyInventorySlot)
                return MoveResult.Refused(MoveRefusal.InvalidSlot);

            if (IsSame(from, to)) return MoveResult.Refused(MoveRefusal.SameSlot);

            Part moving = Get(from);
            if (moving == null) return MoveResult.Refused(MoveRefusal.SourceEmpty);

            PartSlot target = to;
            if (to.IsInventory && to.InventoryIndex == PartSlot.AnyInventorySlot)
            {
                if (from.IsInventory) return MoveResult.Refused(MoveRefusal.SameSlot);
                int free = inventory.FirstFreeSlot;
                if (free < 0) return MoveResult.Refused(MoveRefusal.InventoryFull);
                target = PartSlot.InInventory(free);
            }

            Part other = Get(target);

            if (!target.IsInventory && moving.Type != target.PartType)
                return MoveResult.Refused(MoveRefusal.WrongSlotType);
            if (other != null && !from.IsInventory && other.Type != from.PartType)
                return MoveResult.Refused(MoveRefusal.WrongSlotType);

            Set(target, moving);
            Set(from, other);

            MoveResult result = MoveResult.Ok(moving, other, from, target);
            Action<MoveResult> handler = PartMoved;
            if (handler != null) handler(result);
            return result;
        }

        /// <summary>
        /// Throws away the part in a slot (inventory or ship) and reports the scrap
        /// amount from balance.json. The slot is left empty.
        /// </summary>
        public BinResult Bin(PartSlot slot)
        {
            if (!IsValid(slot)) return BinResult.Refused(MoveRefusal.InvalidSlot);
            if (slot.IsInventory && slot.InventoryIndex == PartSlot.AnyInventorySlot)
                return BinResult.Refused(MoveRefusal.InvalidSlot);

            Part part = Get(slot);
            if (part == null) return BinResult.Refused(MoveRefusal.SourceEmpty);

            Set(slot, null);

            BinResult result = BinResult.Ok(part, slot, scrapPrice);
            Action<BinResult> handler = PartBinned;
            if (handler != null) handler(result);
            return result;
        }

        private bool IsValid(PartSlot slot)
        {
            if (slot == null) return false;
            if (slot.IsInventory)
            {
                if (slot.InventoryIndex == PartSlot.AnyInventorySlot) return true;
                return slot.InventoryIndex >= 0 && slot.InventoryIndex < inventory.SlotCount;
            }
            if (slot.Ship == null) return false;
            int t = (int)slot.PartType;
            return t >= 0 && t < GameRules.PartSlotsPerShip;
        }

        private static bool IsSame(PartSlot a, PartSlot b)
        {
            if (a.IsInventory != b.IsInventory) return false;
            if (a.IsInventory) return a.InventoryIndex == b.InventoryIndex;
            return a.Ship == b.Ship && a.PartType == b.PartType;
        }

        private Part Get(PartSlot slot)
        {
            if (slot.IsInventory) return inventory.GetPart(slot.InventoryIndex);
            return slot.Ship.GetPart(slot.PartType);
        }

        private void Set(PartSlot slot, Part part)
        {
            if (slot.IsInventory) inventory.SetPart(slot.InventoryIndex, part);
            else slot.Ship.SetPart(slot.PartType, part);
        }
    }
}
