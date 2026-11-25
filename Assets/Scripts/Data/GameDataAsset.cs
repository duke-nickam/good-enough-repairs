using UnityEngine;

namespace GoodEnoughRepairs
{
    // Holds the five game data JSON files as text assets.
    // The designer creates it in the editor (Assets > Create > Good Enough Repairs > Game Data)
    // and drops the files from Assets/GameData into the fields.
    [CreateAssetMenu(fileName = "GameData", menuName = "Good Enough Repairs/Game Data")]
    public sealed class GameDataAsset : ScriptableObject
    {
        [Tooltip("Assets/GameData/balance.json")]
        public TextAsset balance;

        [Tooltip("Assets/GameData/content/day_briefs.json")]
        public TextAsset dayBriefs;

        [Tooltip("Assets/GameData/content/mechanic_lines.json")]
        public TextAsset mechanicLines;

        [Tooltip("Assets/GameData/content/part_catalog.json")]
        public TextAsset partCatalog;

        [Tooltip("Assets/GameData/content/ui_text.json")]
        public TextAsset uiText;

        /// <summary>
        /// The most recently loaded game data asset. Used only by the loaders'
        /// no-argument Load() overloads, for callers that were not given the asset.
        /// </summary>
        public static GameDataAsset Current { get; private set; }

        private void OnEnable()
        {
            Current = this;
        }
    }
}
