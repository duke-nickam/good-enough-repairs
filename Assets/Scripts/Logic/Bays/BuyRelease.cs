using System;

namespace GoodEnoughRepairs
{
    /// <summary>Why a buy request was refused.</summary>
    public enum BuyRefusal
    {
        None,
        NullShip,
        InvalidSlot,
        NotDocked,
        /// <summary>The slot holds a part at or above the required level.</summary>
        PartGoodEnough
    }

    /// <summary>Why a release request was refused.</summary>
    public enum ReleaseRefusal
    {
        None,
        NullShip,
        NotDocked
    }

    /// <summary>
    /// Read-only numbers behind the buy warning. When CanBuy is false, Reason says why.
    /// </summary>
    public sealed class BuyPreview
    {
        public bool CanBuy { get; private set; }
        public BuyRefusal Reason { get; private set; }
        /// <summary>The shop price of the new part.</summary>
        public int Price { get; private set; }
        /// <summary>The part now in the slot, or null if the slot is empty.</summary>
        public Part OldPart { get; private set; }
        /// <summary>True when the old part would be binned because the inventory is full.</summary>
        public bool OldPartBinned { get; private set; }
        /// <summary>Scrap paid for the binned old part, otherwise 0.</summary>
        public int ScrapAmount { get; private set; }

        internal BuyPreview(bool canBuy, BuyRefusal reason, int price, Part oldPart, bool binned, int scrap)
        {
            CanBuy = canBuy;
            Reason = reason;
            Price = price;
            OldPart = oldPart;
            OldPartBinned = binned;
            ScrapAmount = scrap;
        }
    }

    /// <summary>Result of a buy command. Refusals never throw and change nothing.</summary>
    public sealed class BuyResult
    {
        public bool Success { get; private set; }
        public BuyRefusal Reason { get; private set; }
        public Ship Ship { get; private set; }
        public PartType PartType { get; private set; }
        /// <summary>The new 100% part now in the slot.</summary>
        public Part NewPart { get; private set; }
        /// <summary>The part that was replaced, or null if the slot was empty.</summary>
        public Part OldPart { get; private set; }
        /// <summary>The shop price paid. 0 when refused. Credits are changed later by the economy.</summary>
        public int Price { get; private set; }
        public bool OldPartBinned { get; private set; }
        public int ScrapAmount { get; private set; }
        /// <summary>The inventory slot of the old part, or -1.</summary>
        public int InventorySlot { get; private set; }

        internal BuyResult(bool success, BuyRefusal reason, Ship ship, PartType type, Part newPart,
            Part oldPart, int price, bool binned, int scrap, int inventorySlot)
        {
            Success = success;
            Reason = reason;
            Ship = ship;
            PartType = type;
            NewPart = newPart;
            OldPart = oldPart;
            Price = price;
            OldPartBinned = binned;
            ScrapAmount = scrap;
            InventorySlot = inventorySlot;
        }
    }

    /// <summary>
    /// Read-only numbers behind the release warning:
    /// net result = payment minus penalty parts.
    /// </summary>
    public sealed class ReleasePreview
    {
        public bool CanRelease { get; private set; }
        public ReleaseRefusal Reason { get; private set; }
        public int Payment { get; private set; }
        /// <summary>Slots that are empty or below the required level.</summary>
        public int PenaltyFills { get; private set; }
        public int PenaltyPrice { get; private set; }
        public int PenaltyTotal { get; private set; }
        public int NetResult { get; private set; }

        internal ReleasePreview(bool can, ReleaseRefusal reason, int payment, int fills, int penaltyPrice)
        {
            CanRelease = can;
            Reason = reason;
            Payment = payment;
            PenaltyFills = fills;
            PenaltyPrice = penaltyPrice;
            PenaltyTotal = fills * penaltyPrice;
            NetResult = payment - PenaltyTotal;
        }
    }

    /// <summary>Result of a release command. Credits are changed later by the economy.</summary>
    public sealed class ReleaseResult
    {
        public bool Success { get; private set; }
        public ReleaseRefusal Reason { get; private set; }
        public Ship Ship { get; private set; }
        /// <summary>The bay the ship left, or -1.</summary>
        public int Bay { get; private set; }
        public int Payment { get; private set; }
        public int PenaltyFills { get; private set; }
        public int PenaltyPrice { get; private set; }
        public int PenaltyTotal { get; private set; }
        /// <summary>Payment minus penalty parts.</summary>
        public int NetResult { get; private set; }
        /// <summary>The part types that were filled with new parts.</summary>
        public PartType[] FilledTypes { get; private set; }

        internal ReleaseResult(bool success, ReleaseRefusal reason, Ship ship, int bay, int payment,
            int fills, int penaltyPrice, PartType[] filled)
        {
            Success = success;
            Reason = reason;
            Ship = ship;
            Bay = bay;
            Payment = payment;
            PenaltyFills = fills;
            PenaltyPrice = penaltyPrice;
            PenaltyTotal = fills * penaltyPrice;
            NetResult = payment - PenaltyTotal;
            FilledTypes = filled;
        }
    }

    /// <summary>
    /// The buy and release commands and their previews. Every command returns a
    /// result with a reason when refused, and never throws. Events fire after the
    /// state has changed. Credits are not tracked here: results and events carry
    /// the amounts.
    /// </summary>
    public sealed class BuyRelease
    {
        private readonly DockingBays bays;
        private readonly Inventory inventory;
        private readonly BalanceData balance;

        /// <summary>A part was bought.</summary>
        public event Action<BuyResult> PartBought;

        /// <summary>A ship was released, with the net result and penalty fills.</summary>
        public event Action<ReleaseResult> ShipReleased;

        public BuyRelease(DockingBays bays, Inventory inventory, BalanceData balance)
        {
            if (bays == null) throw new ArgumentNullException("bays");
            if (inventory == null) throw new ArgumentNullException("inventory");
            if (balance == null) throw new ArgumentNullException("balance");
            this.bays = bays;
            this.inventory = inventory;
            this.balance = balance;
        }

        /// <summary>The buy warning's numbers. Changes nothing.</summary>
        public BuyPreview PreviewBuy(Ship ship, PartType type)
        {
            int price = Prices.NewPartPrice(balance);
            if (ship == null) return new BuyPreview(false, BuyRefusal.NullShip, price, null, false, 0);
            int t = (int)type;
            if (t < 0 || t >= GameRules.PartSlotsPerShip)
                return new BuyPreview(false, BuyRefusal.InvalidSlot, price, null, false, 0);
            if (bays.GetBay(ship) < 0)
                return new BuyPreview(false, BuyRefusal.NotDocked, price, null, false, 0);

            Part old = ship.GetPart(type);
            if (old != null && old.Passes(ship.RequiredLevel))
                return new BuyPreview(false, BuyRefusal.PartGoodEnough, price, old, false, 0);

            bool binned = old != null && inventory.IsFull;
            int scrap = binned ? balance.scrapPrice : 0;
            return new BuyPreview(true, BuyRefusal.None, price, old, binned, scrap);
        }

        /// <summary>
        /// Puts a new 100% part in the slot. Works only for an empty slot or a part
        /// below the ship's required level. The old part goes to the inventory, or is
        /// binned (scrap amount in the result) when the inventory is full.
        /// </summary>
        public BuyResult Buy(Ship ship, PartType type)
        {
            BuyPreview p = PreviewBuy(ship, type);
            if (!p.CanBuy)
                return new BuyResult(false, p.Reason, ship, type, null, null, 0, false, 0, -1);

            Part fresh = new Part(type, Part.MaxCondition);
            int invSlot = -1;
            if (p.OldPart != null && !p.OldPartBinned)
            {
                inventory.TryAdd(p.OldPart, out invSlot);
            }
            ship.SetPart(type, fresh);

            BuyResult result = new BuyResult(true, BuyRefusal.None, ship, type, fresh, p.OldPart,
                p.Price, p.OldPartBinned, p.ScrapAmount, invSlot);
            Action<BuyResult> handler = PartBought;
            if (handler != null) handler(result);
            return result;
        }

        /// <summary>The release warning's numbers: payment minus penalty parts. Changes nothing.</summary>
        public ReleasePreview PreviewRelease(Ship ship)
        {
            int penalty = Prices.PenaltyPrice(balance);
            if (ship == null) return new ReleasePreview(false, ReleaseRefusal.NullShip, 0, 0, penalty);
            if (bays.GetBay(ship) < 0) return new ReleasePreview(false, ReleaseRefusal.NotDocked, 0, 0, penalty);
            return new ReleasePreview(true, ReleaseRefusal.None, ship.Payment, ship.BelowLevelCount, penalty);
        }

        /// <summary>
        /// Sends the ship off. Always possible for a docked ship, also with empty or
        /// weak slots: each is filled with a new part at the penalty price, and the
        /// replaced parts are discarded. The bay is freed.
        /// </summary>
        public ReleaseResult Release(Ship ship)
        {
            ReleasePreview p = PreviewRelease(ship);
            if (!p.CanRelease)
                return new ReleaseResult(false, p.Reason, ship, -1, 0, 0, p.PenaltyPrice, new PartType[0]);

            PartType[] filled = new PartType[p.PenaltyFills];
            int k = 0;
            for (int i = 0; i < GameRules.PartSlotsPerShip; i++)
            {
                PartType type = (PartType)i;
                Part part = ship.GetPart(type);
                if (part != null && part.Passes(ship.RequiredLevel)) continue;
                ship.SetPart(type, new Part(type, Part.MaxCondition));
                filled[k++] = type;
            }

            BayResult removed = bays.Remove(ship);
            ReleaseResult result = new ReleaseResult(true, ReleaseRefusal.None, ship, removed.Bay,
                p.Payment, p.PenaltyFills, p.PenaltyPrice, filled);
            Action<ReleaseResult> handler = ShipReleased;
            if (handler != null) handler(result);
            return result;
        }
    }
}
