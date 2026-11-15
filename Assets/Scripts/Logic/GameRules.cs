namespace GoodEnoughRepairs
{
    /// <summary>
    /// Fixed GDD rules as named constants. Balance numbers (prices, required
    /// levels, scrap value, penalty) are not here; they live in balance.json.
    /// The five part types are named in PartType.
    /// </summary>
    public static class GameRules
    {
        /// <summary>Docking bays at the station.</summary>
        public const int DockingBays = 3;

        /// <summary>Slots in the shared inventory.</summary>
        public const int InventorySlots = 10;

        /// <summary>Salvaged parts listed in the junkyard each day.</summary>
        public const int JunkyardParts = 15;

        /// <summary>Game days the player must survive to win a run.</summary>
        public const int RunDays = 7;

        /// <summary>Part slots on every ship.</summary>
        public const int PartSlotsPerShip = 5;
    }
}
