using UnityEngine;

namespace GoodEnoughRepairs.Data
{
    /// <summary>
    /// Loads economy.json at start. Drag Assets/Data/economy.json into the Economy Json field.
    /// Other systems read EconomyLoader.Current.
    /// </summary>
    public class EconomyLoader : MonoBehaviour
    {
        [SerializeField] private TextAsset economyJson;

        public static EconomyConfig Current { get; private set; }

        private void Awake()
        {
            Current = EconomyConfig.FromJson(economyJson);
            Debug.Log($"Economy loaded ({Current.version}): start {Current.startingCredits} credits, " +
                      $"upkeep {Current.stationUpkeep}/day, {Current.days.Length} days.");
        }
    }
}
