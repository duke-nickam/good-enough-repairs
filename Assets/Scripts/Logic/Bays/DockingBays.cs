using System;

namespace GoodEnoughRepairs
{
    /// <summary>Why a dock or remove request was refused.</summary>
    public enum BayRefusal
    {
        None,
        StationFull,
        AlreadyDocked,
        NullShip,
        NotDocked,
        InvalidBay,
        BayEmpty
    }

    /// <summary>Result of a dock or remove request. Refusals never throw.</summary>
    public sealed class BayResult
    {
        public bool Success { get; private set; }
        public BayRefusal Reason { get; private set; }
        /// <summary>The bay used, or -1 when refused.</summary>
        public int Bay { get; private set; }
        public Ship Ship { get; private set; }

        internal BayResult(bool success, BayRefusal reason, int bay, Ship ship)
        {
            Success = success;
            Reason = reason;
            Bay = bay;
            Ship = ship;
        }

        internal static BayResult Ok(int bay, Ship ship)
        {
            return new BayResult(true, BayRefusal.None, bay, ship);
        }

        internal static BayResult Refused(BayRefusal reason)
        {
            return new BayResult(false, reason, -1, null);
        }
    }

    /// <summary>
    /// The station's docking bays (GameRules.DockingBays). Docks a ship into the
    /// lowest free bay and removes a ship that leaves. Events fire after the state
    /// has changed. Pulling in the next ship is the day cycle's job.
    /// </summary>
    public sealed class DockingBays
    {
        private readonly Ship[] bays = new Ship[GameRules.DockingBays];

        /// <summary>A ship docked: the ship and its bay.</summary>
        public event Action<Ship, int> ShipDocked;

        /// <summary>A ship left: the ship and the bay it left.</summary>
        public event Action<Ship, int> ShipLeft;

        public int BayCount
        {
            get { return bays.Length; }
        }

        public int FreeBayCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < bays.Length; i++) if (bays[i] == null) n++;
                return n;
            }
        }

        public bool IsFull
        {
            get { return FreeBayCount == 0; }
        }

        /// <summary>The ship in a bay, or null if the bay is empty or the index is invalid.</summary>
        public Ship GetShip(int bay)
        {
            if (bay < 0 || bay >= bays.Length) return null;
            return bays[bay];
        }

        /// <summary>Docked ships in bay order. A copy.</summary>
        public Ship[] Ships
        {
            get
            {
                Ship[] list = new Ship[bays.Length - FreeBayCount];
                int k = 0;
                for (int i = 0; i < bays.Length; i++) if (bays[i] != null) list[k++] = bays[i];
                return list;
            }
        }

        /// <summary>The bay of a docked ship, or -1.</summary>
        public int GetBay(Ship ship)
        {
            if (ship == null) return -1;
            for (int i = 0; i < bays.Length; i++) if (bays[i] == ship) return i;
            return -1;
        }

        public BayResult TryDock(Ship ship)
        {
            if (ship == null) return BayResult.Refused(BayRefusal.NullShip);
            if (GetBay(ship) >= 0) return BayResult.Refused(BayRefusal.AlreadyDocked);
            for (int i = 0; i < bays.Length; i++)
            {
                if (bays[i] != null) continue;
                bays[i] = ship;
                Action<Ship, int> handler = ShipDocked;
                if (handler != null) handler(ship, i);
                return BayResult.Ok(i, ship);
            }
            return BayResult.Refused(BayRefusal.StationFull);
        }

        public BayResult Remove(Ship ship)
        {
            if (ship == null) return BayResult.Refused(BayRefusal.NullShip);
            int bay = GetBay(ship);
            if (bay < 0) return BayResult.Refused(BayRefusal.NotDocked);
            return Remove(bay);
        }

        public BayResult Remove(int bay)
        {
            if (bay < 0 || bay >= bays.Length) return BayResult.Refused(BayRefusal.InvalidBay);
            Ship ship = bays[bay];
            if (ship == null) return BayResult.Refused(BayRefusal.BayEmpty);
            bays[bay] = null;
            Action<Ship, int> handler = ShipLeft;
            if (handler != null) handler(ship, bay);
            return BayResult.Ok(bay, ship);
        }
    }
}
