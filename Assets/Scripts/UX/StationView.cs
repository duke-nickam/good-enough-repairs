using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// Entry point of the show layer. Add this component to Main.unity by hand.
    /// Loads the balance and the text, builds the station with its 3 bays, docks
    /// ships on a timer and draws everything in OnGUI. It reacts to the bays' events
    /// and decides no rules: every ship and number comes from Logic, every word
    /// from ui_text.json.
    /// </summary>
    public sealed class StationView : MonoBehaviour
    {
        [SerializeField] private int seed = 1;

        /// <summary>
        /// Optional prefab instance for bay 1. When assigned, it shows the bay 1
        /// ship every frame, centered in the bay, and the old OnGUI ship panel is
        /// not used for that bay. An empty bay hides its view.
        /// </summary>
        public ShipView bay1View;

        /// <summary>Optional prefab instance for bay 2. Works like bay1View.</summary>
        public ShipView bay2View;

        /// <summary>Optional prefab instance for bay 3. Works like bay1View.</summary>
        public ShipView bay3View;

        /// <summary>
        /// Optional inventory panel prefab instance. When assigned, it gets the
        /// shared inventory and the move and bin commands, and runs the drag and drop.
        /// </summary>
        public InventoryView inventoryView;

        /// <summary>
        /// Optional warning dialog prefab instance. When assigned, it gets the buy
        /// and release commands and the mechanic's lines, and the ship view buy and
        /// release buttons use it. Every release goes through it.
        /// </summary>
        public WarningView warningView;

        private const float Margin = 20f;
        private const float Gap = 16f;
        private const float BayHeight = 150f;

        private BalanceData balance;
        private ContentData content;
        private DockingBays bays;
        private BuyRelease buyRelease;
        private System.Random shipRandom;
        private Ship[] shown;
        private ShipPanel[] views;
        private int surplusMargin;
        private int arrivalIntervalMs;
        private float untilNextShipMs;
        private GUIStyle centeredLabel;

        /// <summary>The ship view of a bay (0-2), or null when the bay has none.</summary>
        private ShipView ViewOf(int bay)
        {
            switch (bay)
            {
                case 0: return bay1View;
                case 1: return bay2View;
                case 2: return bay3View;
                default: return null;
            }
        }

        private void Awake()
        {
            // The text is loaded once and shared with every view.
            content = ContentLoader.Load();
            TextKeyView.SetShared(content);

            balance = BalanceLoader.Load();
            if (balance == null)
            {
                Debug.LogError("[Station] Balance could not be loaded. The station will not start.");
                enabled = false;
                return;
            }

            arrivalIntervalMs = Mathf.Max(1, balance.days[0].shipEverySeconds) * 1000;
            surplusMargin = BarBand.SurplusMargin(balance.requiredFighterPercent, balance.requiredFreighterPercent);
            shipRandom = new System.Random(seed);

            bays = new DockingBays();
            shown = new Ship[bays.BayCount];
            views = new ShipPanel[bays.BayCount];
            bays.ShipDocked += OnShipDocked;
            bays.ShipLeft += OnShipLeft;
            untilNextShipMs = 0f;

            // One shared inventory for the move, bin, buy and release commands.
            Inventory inventory = new Inventory();
            buyRelease = new BuyRelease(bays, inventory, balance);
            buyRelease.PartBought += OnPartBought;

            if (inventoryView != null) inventoryView.Bind(new PartMoves(inventory, balance), balance);
            if (warningView != null) warningView.Bind(buyRelease, content);
            for (int bay = 0; bay < bays.BayCount; bay++)
            {
                ShipView v = ViewOf(bay);
                if (v != null) v.Bind(buyRelease, warningView, content);
            }
        }

        private void Update()
        {
            if (bays == null) return;

            for (int bay = 0; bay < shown.Length; bay++)
            {
                ShipView v = ViewOf(bay);
                if (v == null) continue;
                v.Show(shown[bay], surplusMargin);
                if (shown[bay] != null) CenterInBay(v, bay);
            }

            if (bays.IsFull) return;

            untilNextShipMs -= Time.deltaTime * 1000f;
            if (untilNextShipMs > 0f) return;

            untilNextShipMs = arrivalIntervalMs;
            Ship ship = ShipFactory.Create(balance, balance.days[0], shipRandom);
            bays.TryDock(ship);
        }

        /// <summary>
        /// Puts the ship at the center of the bay's rectangle on screen. The ship is
        /// centered on its content bounds (body and bars), the same rule the
        /// prefab builder uses (ShipView.CenterOn).
        /// </summary>
        private static void CenterInBay(ShipView view, int bay)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Rect rect = BayRect(bay);
            Transform t = view.transform;
            float depth = Mathf.Abs(t.position.z - cam.transform.position.z);
            Vector3 screen = new Vector3(rect.center.x, Screen.height - rect.center.y, depth);
            Vector3 target = cam.ScreenToWorldPoint(screen);
            target.z = t.position.z;
            t.position = target;
            view.CenterOn(target);
        }

        private void OnGUI()
        {
            if (bays == null) return;

            if (centeredLabel == null)
            {
                centeredLabel = new GUIStyle(GUI.skin.label);
                centeredLabel.alignment = TextAnchor.MiddleCenter;
            }

            for (int bay = 0; bay < shown.Length; bay++)
            {
                bool hasView = ViewOf(bay) != null;
                if (!(hasView && shown[bay] != null)) DrawBay(bay);

                // A bay with a ship view opens nothing: the view is already the ship.
                Rect rect = BayRect(bay);
                if (!hasView && shown[bay] != null && GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    views[bay] = views[bay] == null ? new ShipPanel(shown[bay], surplusMargin, content) : null;
                }
            }

            for (int bay = 0; bay < views.Length; bay++)
            {
                if (views[bay] == null) continue;
                Ship ship = views[bay].Ship;
                if (views[bay].Draw(ViewRect(bay)))
                {
                    // The only release path: the warning dialog and the release
                    // command. With no dialog connected, the button does nothing.
                    if (warningView != null) warningView.RequestRelease(ship);
                }
            }
        }

        private void OnDestroy()
        {
            if (bays == null) return;
            bays.ShipDocked -= OnShipDocked;
            bays.ShipLeft -= OnShipLeft;
            if (buyRelease != null) buyRelease.PartBought -= OnPartBought;
        }

        private void OnShipDocked(Ship ship, int bay)
        {
            shown[bay] = ship;
        }

        private void OnShipLeft(Ship ship, int bay)
        {
            shown[bay] = null;
            views[bay] = null;
        }

        // A bought part can send the old part to the inventory: redraw the panel.
        private void OnPartBought(BuyResult result)
        {
            if (inventoryView != null) inventoryView.Refresh();
        }

        private void DrawBay(int bay)
        {
            Rect rect = BayRect(bay);
            ShipPanel.DrawPanel(rect);

            float x = rect.x + ShipPanel.Pad;
            float w = rect.width - 2f * ShipPanel.Pad;
            Ship ship = shown[bay];

            if (ship == null)
            {
                GUI.Label(new Rect(x, rect.y + rect.height / 2f - 9f, w, 18f),
                    content.Text(UiKeys.BayEmpty, bay + 1));
                return;
            }

            // Bay 1 is centered both ways: the content block is moved to the middle
            // of the bay and its text is centered. Other bays keep their layout.
            bool center = bay == 0;
            float contentTop = ShipPanel.Pad;
            float contentBottom = 96f + 12f;
            float shift = center ? (BayHeight - (contentBottom - contentTop)) / 2f - contentTop : 0f;
            GUIStyle style = center ? centeredLabel : GUI.skin.label;
            float top = rect.y + shift;

            GUI.Label(new Rect(x, top + ShipPanel.Pad, w, 18f),
                content.Text(UiKeys.BayLabel, bay + 1) + " " + ShipPanel.ShipTypeName(content, ship.Type), style);
            GUI.Label(new Rect(x, top + 32f, w, 18f), content.Text(UiKeys.Payment, ship.Payment), style);
            GUI.Label(new Rect(x, top + 52f, w, 18f), content.Text(UiKeys.Needs, ship.RequiredLevel), style);

            Part weakest = ship.WeakestPart;
            if (weakest != null)
            {
                GUI.Label(new Rect(x, top + 72f, w, 18f),
                    content.Text(UiKeys.Weakest, ShipPanel.PartName(content, weakest.Type), weakest.Condition), style);
                ShipPanel.DrawBar(new Rect(x, top + 96f, w, 12f),
                    weakest.Condition,
                    BarColors.ColorFor(weakest, ship.RequiredLevel, surplusMargin),
                    ship.RequiredLevel);
            }
        }

        private static Rect BayRect(int bay)
        {
            return new Rect(Margin + bay * (ShipPanel.Width + Gap), Margin, ShipPanel.Width, BayHeight);
        }

        private static Rect ViewRect(int bay)
        {
            Rect bayRect = BayRect(bay);
            return new Rect(bayRect.x, Margin + BayHeight + Gap, ShipPanel.Width, ShipPanel.Height);
        }
    }
}
