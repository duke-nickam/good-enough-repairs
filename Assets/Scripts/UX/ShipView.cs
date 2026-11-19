using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// Colors of the condition bars and panels, in one place so the designer can
    /// change them. It only maps a band from BarBand to a color. The decision
    /// itself lives in BarBand.
    /// </summary>
    public static class BarColors
    {
        public static readonly Color Below = new Color(0.85f, 0.20f, 0.18f);
        public static readonly Color Passes = new Color(0.25f, 0.75f, 0.35f);
        public static readonly Color Surplus = new Color(0.25f, 0.55f, 0.95f);
        public static readonly Color Track = new Color(0.10f, 0.10f, 0.12f);
        public static readonly Color Marker = Color.white;
        public static readonly Color Panel = new Color(0.16f, 0.17f, 0.20f, 0.95f);

        /// <summary>Color of a band.</summary>
        public static Color ColorOf(BarBandKind band)
        {
            return ColorOf(band, Below, Passes, Surplus);
        }

        /// <summary>Color of a band, with chosen colors.</summary>
        public static Color ColorOf(BarBandKind band, Color below, Color passes, Color surplus)
        {
            switch (band)
            {
                case BarBandKind.Red: return below;
                case BarBandKind.Blue: return surplus;
                default: return passes;
            }
        }

        /// <summary>Bar color of a part in a ship. A missing part is red.</summary>
        public static Color ColorFor(Part part, int requiredLevel, int surplusMargin)
        {
            return ColorOf(BarBand.Of(part, requiredLevel, surplusMargin));
        }
    }

    /// <summary>
    /// A ship shown with sprites. Put it on a prefab. It shows a ship body and five
    /// part condition bars (in PartTypes.All order: engine, hull plate, shield
    /// generator, thruster, power core). It only displays what it is given and
    /// holds no rules.
    ///
    /// Each bar fill is a SpriteRenderer whose sprite pivot is on the left edge.
    /// Its X scale at start is the full bar (100%); Show scales it to the part's
    /// condition and colors it.
    /// </summary>
    public sealed class ShipView : MonoBehaviour
    {
        [Header("Sprites")]
        public SpriteRenderer body;

        [Tooltip("Five bar fills, in order: engine, hull plate, shield generator, thruster, power core.")]
        public SpriteRenderer[] barFills = new SpriteRenderer[GameRules.PartSlotsPerShip];

        [Header("Colors")]
        public Color belowColor = BarColors.Below;
        public Color passesColor = BarColors.Passes;
        [Tooltip("Blue by default. Switch to purple if the ship art is blue.")]
        public Color surplusColor = BarColors.Surplus;

        private Vector3[] fullScales;

        /// <summary>
        /// Shows the ship. Pass null to hide the view (empty bay). surplusMargin is
        /// how far above the required level a part counts as surplus.
        /// </summary>
        public void Show(Ship ship, int surplusMargin)
        {
            Show(ship, surplusMargin, null);
        }

        /// <summary>
        /// Shows the ship, with optional sample conditions for the prefab builder's
        /// screenshot. conditionOverrides has one entry per part slot (PartTypes.All
        /// order): 0-100 to show that condition instead of the part's, or -1 to show
        /// the real part. Pass null to show the real parts. The ship is not changed.
        /// </summary>
        public void Show(Ship ship, int surplusMargin, int[] conditionOverrides)
        {
            if (ship == null)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            CaptureScales();

            PartType[] types = PartTypes.All;
            int count = barFills == null ? 0 : barFills.Length;
            for (int i = 0; i < types.Length && i < count; i++)
            {
                SpriteRenderer fill = barFills[i];
                if (fill == null) continue;

                Part part = ship.GetPart(types[i]);
                bool overridden = conditionOverrides != null && i < conditionOverrides.Length &&
                    conditionOverrides[i] >= Part.MinCondition;

                int condition;
                BarBandKind band;
                if (overridden)
                {
                    condition = Mathf.Clamp(conditionOverrides[i], Part.MinCondition, Part.MaxCondition);
                    band = BarBand.Of(condition, ship.RequiredLevel, surplusMargin);
                }
                else
                {
                    condition = part == null ? Part.MinCondition : part.Condition;
                    band = BarBand.Of(part, ship.RequiredLevel, surplusMargin);
                }
                float fraction = Mathf.Clamp01(condition / (float)Part.MaxCondition);

                Vector3 full = fullScales[i];
                fill.transform.localScale = new Vector3(full.x * fraction, full.y, full.z);
                fill.color = BarColors.ColorOf(band, belowColor, passesColor, surplusColor);
            }
        }

        /// <summary>
        /// World bounds of the ship's content: the body and all bar fills at full
        /// length (so the bounds do not change with the conditions shown). The one
        /// place that defines what "the ship" covers when it is centered. Returns
        /// false when there is nothing to measure.
        /// </summary>
        public bool TryGetContentBounds(out Bounds bounds)
        {
            bounds = new Bounds(transform.position, Vector3.zero);
            bool any = false;

            if (body != null && body.sprite != null)
            {
                bounds = body.bounds;
                any = true;
            }

            CaptureScales();
            int count = barFills == null ? 0 : barFills.Length;
            for (int i = 0; i < count; i++)
            {
                SpriteRenderer fill = barFills[i];
                if (fill == null || fill.sprite == null) continue;

                Transform ft = fill.transform;
                Matrix4x4 parentToWorld = ft.parent == null ? Matrix4x4.identity : ft.parent.localToWorldMatrix;
                Matrix4x4 fullToWorld = parentToWorld *
                    Matrix4x4.TRS(ft.localPosition, ft.localRotation, fullScales[i]);

                Bounds local = fill.sprite.bounds;
                Vector3 c = local.center;
                Vector3 e = local.extents;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        Vector3 corner = fullToWorld.MultiplyPoint3x4(
                            new Vector3(c.x + sx * e.x, c.y + sy * e.y, c.z));
                        if (!any)
                        {
                            bounds = new Bounds(corner, Vector3.zero);
                            any = true;
                        }
                        else
                        {
                            bounds.Encapsulate(corner);
                        }
                    }
                }
            }
            return any;
        }

        /// <summary>
        /// Moves the ship so the center of its content bounds (TryGetContentBounds)
        /// is at worldPoint, in X and Y. Used by both StationView and the prefab
        /// builder, so they center the ship the same way.
        /// </summary>
        public void CenterOn(Vector3 worldPoint)
        {
            Bounds bounds;
            if (!TryGetContentBounds(out bounds)) return;
            Vector3 offset = worldPoint - bounds.center;
            offset.z = 0f;
            transform.position += offset;
        }

        private void CaptureScales()
        {
            if (fullScales != null && barFills != null && fullScales.Length == barFills.Length) return;
            int count = barFills == null ? 0 : barFills.Length;
            fullScales = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                fullScales[i] = barFills[i] == null ? Vector3.one : barFills[i].transform.localScale;
            }
        }
    }

    /// <summary>
    /// The open ship view drawn with OnGUI: five part slots, each with a condition
    /// bar, and a release button. Drawn over its bay. It reads the Ship it was
    /// given and decides nothing.
    /// </summary>
    public sealed class ShipPanel
    {
        public const float Width = 260f;
        public const float Height = 240f;
        public const float Pad = 8f;

        private const float HeaderHeight = 34f;
        private const float RowHeight = 34f;
        private const float ButtonHeight = 26f;

        private readonly Ship ship;
        private readonly int surplusMargin;

        public ShipPanel(Ship ship, int surplusMargin)
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
            float fillWidth = rect.width * Mathf.Clamp01(percent / (float)Part.MaxCondition);
            GUI.color = BarColors.Track;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x, rect.y, fillWidth, rect.height), Texture2D.whiteTexture);
            GUI.color = BarColors.Marker;
            float markerX = rect.x + rect.width * Mathf.Clamp01(requiredLevel / (float)Part.MaxCondition);
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
