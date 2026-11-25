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

        // TEMPORARY: the last asset that was loaded. Only the no-argument
        // ContentLoader.Load() uses it, because TextKeyView.cs (not in this task)
        // still calls that. Remove it with that overload once TextKeyView passes the asset.
        public static GameDataAsset Current { get; private set; }

        private void OnEnable()
        {
            Current = this;
        }
    }
}
