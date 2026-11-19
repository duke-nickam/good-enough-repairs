using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// Entry point of the show layer. Add this component to Main.unity by hand.
    /// Loads the balance, builds the station with its 3 bays, docks ships on a
    /// timer and draws everything in OnGUI. It reacts to the bays' events and
    /// decides no rules: every ship and number comes from Logic.
    /// </summary>
    public sealed class StationView : MonoBehaviour
    {
        [SerializeField] private int seed = 1;

        /// <summary>
        /// Optional prefab instance for the first bay. When assigned, it shows the
        /// bay 1 ship every frame, centered in the bay, and OnGUI no longer draws
        /// that bay's ship. When null, OnGUI draws it as before.
        /// </summary>
        public ShipView bay1View;

        /// <summary>
        /// Optional inventory panel prefab instance. When assigned, it gets the
        /// shared inventory and the move and bin commands, and runs the drag and drop.
        /// </summary>
        public InventoryView inventoryView;

        private const float Margin = 20f;
        private const float Gap = 16f;
        private const float BayHeight = 150f;

        private BalanceData balance;
        private DockingBays bays;
        private System.Random shipRandom;
        private Ship[] shown;
        private ShipPanel[] views;
        private int surplusMargin;
        private int arrivalIntervalMs;
        private float untilNextShipMs;
        private GUIStyle centeredLabel;

        private void Awake()
        {
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

            if (inventoryView != null) inventoryView.Bind(new PartMoves(new Inventory(), balance), balance);
        }

        private void Update()
        {
            if (bays == null) return;

            if (bay1View != null)
            {
                bay1View.Show(shown[0], surplusMargin);
                if (shown[0] != null) CenterInBay1(bay1View);
            }

            if (bays.IsFull) return;

            untilNextShipMs -= Time.deltaTime * 1000f;
            if (untilNextShipMs > 0f) return;

            untilNextShipMs = arrivalIntervalMs;
            Ship ship = ShipFactory.Create(balance, balance.days[0], shipRandom);
            bays.TryDock(ship);
        }

        /// <summary>
        /// Puts the ship at the center of bay 1's rectangle on screen. The ship is
        /// centered on its content bounds (body and bars), the same rule the
        /// prefab builder uses (ShipView.CenterOn).
        /// </summary>
        private static void CenterInBay1(ShipView view)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Rect rect = BayRect(0);
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
                if (!(bay == 0 && bay1View != null)) DrawBay(bay);
                Rect rect = BayRect(bay);
                if (shown[bay] != null && GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    views[bay] = views[bay] == null ? new ShipPanel(shown[bay], surplusMargin) : null;
                }
            }

            for (int bay = 0; bay < views.Length; bay++)
            {
                if (views[bay] == null) continue;
                if (views[bay].Draw(ViewRect(bay)))
                {
                    bays.Remove(bay);
                }
            }
        }

        private void OnDestroy()
        {
            if (bays == null) return;
            bays.ShipDocked -= OnShipDocked;
            bays.ShipLeft -= OnShipLeft;
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

        private void DrawBay(int bay)
        {
            Rect rect = BayRect(bay);
            ShipPanel.DrawPanel(rect);

            float x = rect.x + ShipPanel.Pad;
            float w = rect.width - 2f * ShipPanel.Pad;
            Ship ship = shown[bay];

            if (ship == null)
            {
                GUI.Label(new Rect(x, rect.y + rect.height / 2f - 9f, w, 18f), "Bay " + (bay + 1) + ": empty");
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
                "Bay " + (bay + 1) + ": " + ShipPanel.ShipTypeName(ship.Type), style);
            GUI.Label(new Rect(x, top + 32f, w, 18f), "Payment " + ship.Payment, style);
            GUI.Label(new Rect(x, top + 52f, w, 18f), "Needs " + ship.RequiredLevel + "%", style);

            Part weakest = ship.WeakestPart;
            if (weakest != null)
            {
                GUI.Label(new Rect(x, top + 72f, w, 18f),
                    "Weakest: " + ShipPanel.PartName(weakest.Type) + " " + weakest.Condition + "%", style);
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
