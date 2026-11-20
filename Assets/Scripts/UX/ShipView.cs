using System.Collections.Generic;
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
    /// Helpers that connect sprites on screen with IMGUI: the pointer in world
    /// space, and a sprite's rectangle in IMGUI space (for text drawn over it).
    /// </summary>
    public static class SpriteScreen
    {
        /// <summary>World point under an IMGUI mouse position. False without a camera.</summary>
        public static bool TryWorld(Vector2 guiPoint, out Vector3 world)
        {
            world = Vector3.zero;
            Camera cam = Camera.main;
            if (cam == null) return false;
            float depth = Mathf.Abs(cam.transform.position.z);
            world = cam.ScreenToWorldPoint(new Vector3(guiPoint.x, Screen.height - guiPoint.y, depth));
            world.z = 0f;
            return true;
        }

        /// <summary>The sprite's bounds as an IMGUI rectangle. False when it cannot be shown.</summary>
        public static bool TryRect(SpriteRenderer sr, out Rect rect)
        {
            rect = new Rect();
            Camera cam = Camera.main;
            if (cam == null || sr == null || sr.sprite == null || !sr.gameObject.activeInHierarchy) return false;
            Bounds b = sr.bounds;
            Vector3 a = cam.WorldToScreenPoint(b.min);
            Vector3 c = cam.WorldToScreenPoint(b.max);
            float xMin = Mathf.Min(a.x, c.x);
            float xMax = Mathf.Max(a.x, c.x);
            float yMin = Mathf.Min(a.y, c.y);
            float yMax = Mathf.Max(a.y, c.y);
            rect = new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
            return true;
        }
    }

    /// <summary>
    /// Shared drag helper for sprites. A drag lifts a sprite (and its children)
    /// above the others and moves it with the pointer. End puts it back exactly
    /// where it was, which is the snap back of a refused move. No rules here.
    /// </summary>
    public sealed class DragVisual
    {
        private const int RaisedBy = 100;

        private readonly Transform target;
        private readonly Vector3 homeLocal;
        private readonly Vector3 grabOffset;
        private readonly SpriteRenderer[] renderers;
        private readonly int[] orders;

        public DragVisual(Transform target, Vector3 pointerWorld)
        {
            this.target = target;
            homeLocal = target.localPosition;
            grabOffset = target.position - pointerWorld;
            grabOffset.z = 0f;
            renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
            orders = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                orders[i] = renderers[i].sortingOrder;
                renderers[i].sortingOrder = orders[i] + RaisedBy;
            }
        }

        /// <summary>Moves the dragged sprite so it keeps its offset from the pointer.</summary>
        public void MoveTo(Vector3 pointerWorld)
        {
            if (target == null) return;
            Vector3 p = pointerWorld + grabOffset;
            p.z = target.position.z;
            target.position = p;
        }

        /// <summary>Puts the sprite back at its home position and sorting order.</summary>
        public void End()
        {
            if (target == null) return;
            target.localPosition = homeLocal;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null) renderers[i].sortingOrder = orders[i];
            }
        }

        /// <summary>True when the world point is inside the sprite's bounds (X and Y).</summary>
        public static bool Hits(SpriteRenderer area, Vector3 world)
        {
            if (area == null || area.sprite == null || !area.gameObject.activeInHierarchy) return false;
            Bounds b = area.bounds;
            return world.x >= b.min.x && world.x <= b.max.x && world.y >= b.min.y && world.y <= b.max.y;
        }
    }

    /// <summary>
    /// A ship shown with sprites. Put it on a prefab. It shows a ship body and five
    /// part condition bars (in PartTypes.All order: engine, hull plate, shield
    /// generator, thruster, power core), a buy button next to each slot and a
    /// release button. It only displays what it is given and holds no rules: the
    /// buy buttons are active when the #101 buy preview says CanBuy, and a click
    /// asks the WarningView to show the warning.
    ///
    /// All words come from ui_text.json. Text elements on the prefab (TextKeyView)
    /// get their numbers from here; a label with no text element is drawn with OnGUI.
    ///
    /// Each bar fill is a SpriteRenderer whose sprite pivot is on the left edge.
    /// Its X scale at start is the full bar (100%); Show scales it to the part's
    /// condition and colors it.
    ///
    /// slotAreas are the five part slots as drag handles and drop targets, in the
    /// same order. The drag itself is run by InventoryView.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ShipView : MonoBehaviour
    {
        private static readonly List<ShipView> active = new List<ShipView>();

        /// <summary>The ship views that are on screen now (active).</summary>
        public static IReadOnlyList<ShipView> Active
        {
            get { return active; }
        }

        [Header("Sprites")]
        public SpriteRenderer body;

        [Tooltip("Five bar fills, in order: engine, hull plate, shield generator, thruster, power core.")]
        public SpriteRenderer[] barFills = new SpriteRenderer[GameRules.PartSlotsPerShip];

        [Header("Drag and drop")]
        [Tooltip("Five part slot sprites, same order. Each is a drag handle (its bounds are the hit area) and a drop target.")]
        public SpriteRenderer[] slotAreas = new SpriteRenderer[GameRules.PartSlotsPerShip];

        [Header("Buy and release")]
        [Tooltip("Five buy button sprites, same order. Each is a click area. Active only for an empty or weak slot.")]
        public SpriteRenderer[] buyButtons = new SpriteRenderer[GameRules.PartSlotsPerShip];

        [Tooltip("The release button sprite. Click area of the release button.")]
        public SpriteRenderer releaseButton;

        public Color buyActiveColor = Color.white;
        public Color buyInactiveColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);

        [Header("Text elements (optional)")]
        [Tooltip("Five text elements for the buy buttons, same order. Key buy_button, {0} is the price.")]
        public TextKeyView[] buyTexts = new TextKeyView[GameRules.PartSlotsPerShip];

        [Tooltip("Text on the release button (key release_button). Without it the label is drawn with OnGUI.")]
        public TextKeyView releaseText;

        [Tooltip("Ship type name. This view sets its key (type_fighter or type_freighter).")]
        public TextKeyView typeText;

        [Tooltip("Payment. Key payment, {0} is the payment. This view sets the number.")]
        public TextKeyView paymentText;

        [Tooltip("Required level. Key needs, {0} is the level. This view sets the number.")]
        public TextKeyView requiredText;

        [Header("Colors")]
        public Color belowColor = BarColors.Below;
        public Color passesColor = BarColors.Passes;
        [Tooltip("Blue by default. Switch to purple if the ship art is blue.")]
        public Color surplusColor = BarColors.Surplus;

        private Vector3[] fullScales;
        private BuyRelease buyRelease;
        private WarningView warning;
        private ContentData content;
        private GUIStyle labelStyle;

        /// <summary>The ship shown now, or null for an empty bay.</summary>
        public Ship CurrentShip { get; private set; }

        private ContentData Content
        {
            get { return content ?? TextKeyView.Shared; }
        }

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        /// <summary>
        /// Connects the buy and release buttons to the commands and the shared
        /// warning dialog. Call again to connect to others.
        /// </summary>
        public void Bind(BuyRelease commands, WarningView warningView)
        {
            Bind(commands, warningView, null);
        }

        /// <summary>
        /// Like Bind, and also sets the text. With null content the shared text of
        /// TextKeyView is used.
        /// </summary>
        public void Bind(BuyRelease commands, WarningView warningView, ContentData contentData)
        {
            buyRelease = commands;
            warning = warningView;
            content = contentData;
            RefreshButtons();
        }

        private void Update()
        {
            RefreshButtons();
        }

        /// <summary>True when the buy button of slot i (PartTypes.All order) is active.</summary>
        public bool IsBuyActive(int i)
        {
            if (buyRelease == null || CurrentShip == null) return false;
            if (i < 0 || i >= GameRules.PartSlotsPerShip) return false;
            return buyRelease.PreviewBuy(CurrentShip, PartTypes.All[i]).CanBuy;
        }

        private void RefreshButtons()
        {
            if (buyRelease == null || CurrentShip == null) return;

            if (buyTexts != null)
            {
                for (int i = 0; i < buyTexts.Length && i < GameRules.PartSlotsPerShip; i++)
                {
                    if (buyTexts[i] == null) continue;
                    BuyPreview p = buyRelease.PreviewBuy(CurrentShip, PartTypes.All[i]);
                    buyTexts[i].SetArgs(p.Price);
                }
            }

            if (buyButtons == null) return;
            for (int i = 0; i < buyButtons.Length && i < GameRules.PartSlotsPerShip; i++)
            {
                SpriteRenderer b = buyButtons[i];
                if (b == null) continue;
                b.color = IsBuyActive(i) ? buyActiveColor : buyInactiveColor;
            }
        }

        // Sets the keys and numbers of the optional text elements.
        private void UpdateTexts(Ship ship)
        {
            if (typeText != null) typeText.SetKey(UiKeys.ShipTypeKey(ship.Type));
            if (paymentText != null) paymentText.SetArgs(ship.Payment);
            if (requiredText != null) requiredText.SetArgs(ship.RequiredLevel);
        }

        // Clicks come from IMGUI like the drag and drop. This runs early (execution
        // order), and a used event is not seen by StationView's bay click.
        private void OnGUI()
        {
            Event e = Event.current;
            if (e == null || buyRelease == null || CurrentShip == null) return;

            if (e.type == EventType.Repaint)
            {
                DrawLabels();
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (warning == null || warning.IsOpen) return;
                Vector3 world;
                if (SpriteScreen.TryWorld(e.mousePosition, out world)) HandleClick(world, e);
            }
        }

        private void HandleClick(Vector3 world, Event e)
        {
            int count = buyButtons == null ? 0 : buyButtons.Length;
            for (int i = 0; i < count && i < GameRules.PartSlotsPerShip; i++)
            {
                if (!DragVisual.Hits(buyButtons[i], world)) continue;
                e.Use();
                if (IsBuyActive(i)) warning.RequestBuy(CurrentShip, PartTypes.All[i]);
                return;
            }

            if (DragVisual.Hits(releaseButton, world))
            {
                e.Use();
                warning.RequestRelease(CurrentShip);
            }
        }

        private void DrawLabels()
        {
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label);
                labelStyle.alignment = TextAnchor.MiddleCenter;
            }

            ContentData text = Content;
            int count = buyButtons == null ? 0 : buyButtons.Length;
            for (int i = 0; i < count && i < GameRules.PartSlotsPerShip; i++)
            {
                // A text element on the prefab shows this label instead.
                if (buyTexts != null && i < buyTexts.Length && buyTexts[i] != null) continue;
                Rect r;
                if (!SpriteScreen.TryRect(buyButtons[i], out r)) continue;
                BuyPreview p = buyRelease.PreviewBuy(CurrentShip, PartTypes.All[i]);
                GUI.Label(r, text.Text(UiKeys.BuyButton, p.Price), labelStyle);
            }

            Rect rr;
            if (releaseText == null && SpriteScreen.TryRect(releaseButton, out rr))
            {
                GUI.Label(rr, text.Text(UiKeys.ReleaseButton), labelStyle);
            }
        }

        /// <summary>Index (PartTypes.All order) of the slot under a world point, or -1.</summary>
        public int SlotAt(Vector3 world)
        {
            int count = slotAreas == null ? 0 : slotAreas.Length;
            for (int i = 0; i < count && i < GameRules.PartSlotsPerShip; i++)
            {
                if (DragVisual.Hits(slotAreas[i], world)) return i;
            }
            return -1;
        }

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
            CurrentShip = ship;
            if (ship == null)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            CaptureScales();
            UpdateTexts(ship);

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

            RefreshButtons();
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
    /// given and decides nothing. All words come from ui_text.json.
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
        private readonly ContentData content;

        public ShipPanel(Ship ship, int surplusMargin, ContentData content)
        {
            this.ship = ship;
            this.surplusMargin = surplusMargin;
            this.content = content ?? TextKeyView.Shared;
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
                ShipTypeName(content, ship.Type) + " " + content.Text(UiKeys.Needs, ship.RequiredLevel));

            PartType[] types = PartTypes.All;
            float y = rect.y + HeaderHeight;
            for (int i = 0; i < types.Length; i++)
            {
                PartType type = types[i];
                Part part = ship.GetPart(type);
                string value = part == null ? content.Text(UiKeys.PartEmpty) : part.Condition.ToString();
                GUI.Label(new Rect(x, y, w, 16f), PartName(content, type) + "  " + value);
                DrawBar(new Rect(x, y + 17f, w, 10f),
                    part == null ? 0 : part.Condition,
                    BarColors.ColorFor(part, ship.RequiredLevel, surplusMargin),
                    ship.RequiredLevel);
                y += RowHeight;
            }

            return GUI.Button(new Rect(x, y + 2f, w, ButtonHeight), content.Text(UiKeys.ReleaseButton));
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

        /// <summary>Name of a ship type, from ui_text.json.</summary>
        public static string ShipTypeName(ContentData content, ShipType type)
        {
            return (content ?? TextKeyView.Shared).Text(UiKeys.ShipTypeKey(type));
        }

        /// <summary>Name of a part type, from ui_text.json.</summary>
        public static string PartName(ContentData content, PartType type)
        {
            return (content ?? TextKeyView.Shared).Text(UiKeys.PartKey(type));
        }
    }
}
