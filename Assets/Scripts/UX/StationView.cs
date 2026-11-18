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
        /// bay 1 ship every frame and OnGUI no longer draws that bay's ship. When
        /// null, OnGUI draws it as before.
        /// </summary>
        public ShipView bay1View;

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
            surplusMargin = Mathf.Max(0, balance.requiredFighterPercent - balance.requiredFreighterPercent);
            shipRandom = new System.Random(seed);

            bays = new DockingBays();
            shown = new Ship[bays.BayCount];
            views = new ShipPanel[bays.BayCount];
            bays.ShipDocked += OnShipDocked;
            bays.ShipLeft += OnShipLeft;
            untilNextShipMs = 0f;
        }

        private void Update()
        {
            if (bays == null) return;

            if (bay1View != null) bay1View.Show(shown[0], surplusMargin);

            if (bays.IsFull) return;

            untilNextShipMs -= Time.deltaTime * 1000f;
            if (untilNextShipMs > 0f) return;

            untilNextShipMs = arrivalIntervalMs;
            Ship ship = ShipFactory.Create(balance, balance.days[0], shipRandom);
            bays.TryDock(ship);
        }

        private void OnGUI()
        {
            if (bays == null) return;

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

            GUI.Label(new Rect(x, rect.y + ShipPanel.Pad, w, 18f),
                "Bay " + (bay + 1) + ": " + ShipPanel.ShipTypeName(ship.Type));
            GUI.Label(new Rect(x, rect.y + 32f, w, 18f), "Payment " + ship.Payment);
            GUI.Label(new Rect(x, rect.y + 52f, w, 18f), "Needs " + ship.RequiredLevel + "%");

            Part weakest = ship.WeakestPart;
            if (weakest != null)
            {
                GUI.Label(new Rect(x, rect.y + 72f, w, 18f),
                    "Weakest: " + ShipPanel.PartName(weakest.Type) + " " + weakest.Condition + "%");
                ShipPanel.DrawBar(new Rect(x, rect.y + 96f, w, 12f),
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
