using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// Colors of the condition bars and panels, in one place so the designer can
    /// change them. A bar is colored against the required level of the ship the
    /// part is in: red below it, green when it passes, and the surplus color when
    /// the part is far enough above it to be worth harvesting.
    /// </summary>
    public static class BarColors
    {
        public static readonly Color Below = new Color(0.85f, 0.20f, 0.18f);
        public static readonly Color Passes = new Color(0.25f, 0.75f, 0.35f);
        public static readonly Color Surplus = new Color(0.60f, 0.40f, 0.95f);
        public static readonly Color Track = new Color(0.10f, 0.10f, 0.12f);
        public static readonly Color Marker = Color.white;
        public static readonly Color Panel = new Color(0.16f, 0.17f, 0.20f, 0.95f);

        /// <summary>
        /// Bar color of a part in a ship. A missing part counts as below the level.
        /// surplusMargin is how far above the required level a part must be to
        /// count as surplus.
        /// </summary>
        public static Color ColorFor(Part part, int requiredLevel, int surplusMargin)
        {
            if (part == null || !part.Passes(requiredLevel)) return Below;
            if (part.Condition >= requiredLevel + surplusMargin) return Surplus;
            return Passes;
        }
    }

    /// <summary>
    /// The open ship view: five part slots, each with a condition bar, and a
    /// release button. Drawn over its bay. It reads the Ship it was given and
    /// decides nothing.
    /// </summary>
    public sealed class ShipView
    {
        public const float Width = 260f;
        public const float Height = 240f;
        public const float Pad = 8f;

        private const float HeaderHeight = 34f;
        private const float RowHeight = 34f;
        private const float ButtonHeight = 26f;

        private readonly Ship ship;
        private readonly int surplusMargin;

        public ShipView(Ship ship, int surplusMargin)
        {
            this.ship = ship;
            this.surplusMargin = surplusMargin;
        }

        public Ship Ship
        {
            get { return ship; }
        }

        /// <summary>Draws the view in rect. Returns true when the release button was clicked.</summary>
        public bool Draw(Rect rect)
        {
            DrawPanel(rect);

            float x = rect.x + Pad;
            float w = rect.width - 2f * Pad;
            GUI.Label(new Rect(x, rect.y + Pad, w, 18f),
                ShipTypeName(ship.Type) + ", needs " + ship.RequiredLevel + "%");

            PartType[] types = PartTypes.All;
            float y = rect.y + HeaderHeight;
            for (int i = 0; i < types.Length; i++)
            {
                PartType type = types[i];
                Part part = ship.GetPart(type);
                string value = part == null ? "empty" : part.Condition + "%";
                GUI.Label(new Rect(x, y, w, 16f), PartName(type) + "  " + value);
                DrawBar(new Rect(x, y + 17f, w, 10f),
                    part == null ? 0 : part.Condition,
                    BarColors.ColorFor(part, ship.RequiredLevel, surplusMargin),
                    ship.RequiredLevel);
                y += RowHeight;
            }

            return GUI.Button(new Rect(x, y + 2f, w, ButtonHeight), "Release ship");
        }

        /// <summary>Draws a bar filled to percent (0-100), with a marker at the required level.</summary>
        public static void DrawBar(Rect rect, int percent, Color fill, int requiredLevel)
        {
            float fillWidth = rect.width * Mathf.Clamp01(percent / 100f);
            GUI.color = BarColors.Track;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x, rect.y, fillWidth, rect.height), Texture2D.whiteTexture);
            GUI.color = BarColors.Marker;
            float markerX = rect.x + rect.width * Mathf.Clamp01(requiredLevel / 100f);
            GUI.DrawTexture(new Rect(markerX - 1f, rect.y - 2f, 2f, rect.height + 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>Draws a plain panel background.</summary>
        public static void DrawPanel(Rect rect)
        {
            GUI.color = BarColors.Panel;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        public static string ShipTypeName(ShipType type)
        {
            return type == ShipType.Fighter ? "Fighter" : "Freighter";
        }

        public static string PartName(PartType type)
        {
            switch (type)
            {
                case PartType.Engine: return "Engine";
                case PartType.HullPlate: return "Hull plate";
                case PartType.ShieldGenerator: return "Shield generator";
                case PartType.Thruster: return "Thruster";
                case PartType.PowerCore: return "Power core";
                default: return "Part";
            }
        }
    }
}
