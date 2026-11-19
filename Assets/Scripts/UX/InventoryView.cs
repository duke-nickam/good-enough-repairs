using System;
using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// The parts of one inventory slot on the panel prefab. A plain data class, so
    /// it can live in this file. Every field is optional.
    /// </summary>
    [Serializable]
    public sealed class InventorySlotRefs
    {
        [Tooltip("Sprite that is the slot's drag handle and drop target. Its bounds are the hit area.")]
        public SpriteRenderer area;

        [Tooltip("Optional. What moves with the pointer when the part is dragged. Empty means the area sprite.")]
        public Transform dragRoot;

        [Tooltip("Condition bar fill. Sprite pivot on the left edge. Its X scale at start is the full bar (100%).")]
        public SpriteRenderer fill;

        [Tooltip("Mark at the freighter's required level. Same parent as the fill.")]
        public Transform freighterMark;

        [Tooltip("Mark at the fighter's required level. Same parent as the fill.")]
        public Transform fighterMark;
    }

    /// <summary>
    /// The inventory panel, on a prefab. It shows the stored parts with condition
    /// bars (marks at the freighter and fighter levels from balance.json) and a bin
    /// target. It also runs the drag and drop for the whole station: a part can be
    /// dragged from an inventory slot or from a ship slot (ShipView.slotAreas) to a
    /// ship slot, an inventory slot, the panel (first free slot) or the bin.
    ///
    /// It holds no rules and no copy of the state. A drop calls PartMoves.Move or
    /// PartMoves.Bin. A refused move changes nothing, and the dragged sprite
    /// snaps back to where it was. The panel redraws on the PartMoved and
    /// PartBinned events.
    ///
    /// Bar marks: a mark is placed at fill X + full fill scale X * level / 100, so
    /// the bar sprite must be 1 world unit wide at scale 1 (like the ship bars).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class InventoryView : MonoBehaviour
    {
        [Tooltip("One entry per inventory slot (10), in slot order.")]
        public InventorySlotRefs[] slots = MakeSlots();

        [Tooltip("Dropping a part on this sprite bins it at once.")]
        public SpriteRenderer binArea;

        [Tooltip("Optional. Dropping a part on this sprite (not on a slot) puts it in the first free slot.")]
        public SpriteRenderer panelArea;

        private PartMoves moves;
        private int freighterLevel;
        private int fighterLevel;
        private Vector3[] fullScales;
        private DragVisual drag;
        private PartSlot dragFrom;

        private static InventorySlotRefs[] MakeSlots()
        {
            InventorySlotRefs[] result = new InventorySlotRefs[GameRules.InventorySlots];
            for (int i = 0; i < result.Length; i++) result[i] = new InventorySlotRefs();
            return result;
        }

        /// <summary>
        /// Connects the panel to the move and bin commands and the balance (for the
        /// bar marks), then draws it. Call again to connect to other commands.
        /// </summary>
        public void Bind(PartMoves partMoves, BalanceData balance)
        {
            if (moves != null)
            {
                moves.PartMoved -= OnMoved;
                moves.PartBinned -= OnBinned;
            }
            moves = partMoves;
            if (balance != null)
            {
                freighterLevel = balance.requiredFreighterPercent;
                fighterLevel = balance.requiredFighterPercent;
            }
            if (moves != null)
            {
                moves.PartMoved += OnMoved;
                moves.PartBinned += OnBinned;
            }
            Refresh();
        }

        /// <summary>Redraws every slot from the inventory.</summary>
        public void Refresh()
        {
            CaptureScales();
            int count = slots == null ? 0 : slots.Length;
            for (int i = 0; i < count; i++)
            {
                InventorySlotRefs s = slots[i];
                if (s == null) continue;

                Part part = moves == null ? null : moves.Inventory.GetPart(i);
                PlaceMarks(s, i);
                if (s.fill == null) continue;

                if (part == null)
                {
                    s.fill.enabled = false;
                    continue;
                }
                s.fill.enabled = true;
                float fraction = Mathf.Clamp01(part.Condition / (float)Part.MaxCondition);
                Vector3 full = fullScales[i];
                s.fill.transform.localScale = new Vector3(full.x * fraction, full.y, full.z);
                s.fill.color = ColorOf(part.Condition);
            }
        }

        private Color ColorOf(int condition)
        {
            if (condition < freighterLevel) return BarColors.Below;
            if (condition < fighterLevel) return BarColors.Passes;
            return BarColors.Surplus;
        }

        private void PlaceMarks(InventorySlotRefs s, int index)
        {
            if (s.fill == null) return;
            Vector3 fillPos = s.fill.transform.localPosition;
            float width = fullScales[index].x;
            PlaceMark(s.freighterMark, fillPos, width, freighterLevel);
            PlaceMark(s.fighterMark, fillPos, width, fighterLevel);
        }

        private static void PlaceMark(Transform mark, Vector3 fillPos, float width, int level)
        {
            if (mark == null) return;
            Vector3 p = mark.localPosition;
            p.x = fillPos.x + width * Mathf.Clamp01(level / (float)Part.MaxCondition);
            mark.localPosition = p;
        }

        private void CaptureScales()
        {
            int count = slots == null ? 0 : slots.Length;
            if (fullScales != null && fullScales.Length == count) return;
            fullScales = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                InventorySlotRefs s = slots[i];
                fullScales[i] = s == null || s.fill == null ? Vector3.one : s.fill.transform.localScale;
            }
        }

        private void OnMoved(MoveResult result)
        {
            Refresh();
        }

        private void OnBinned(BinResult result)
        {
            Refresh();
        }

        private void OnDestroy()
        {
            if (moves == null) return;
            moves.PartMoved -= OnMoved;
            moves.PartBinned -= OnBinned;
        }

        private void OnDisable()
        {
            CancelDrag();
        }

        // Drag and drop. Mouse events come from IMGUI, which works with any input
        // backend. This runs before StationView's OnGUI (execution order), so a
        // click on a part handle is used here first.
        private void OnGUI()
        {
            Event e = Event.current;
            if (moves == null || e == null) return;

            if (e.type == EventType.MouseDown && e.button == 0 && drag == null)
            {
                Vector3 world;
                if (TryWorld(e.mousePosition, out world)) TryBeginDrag(world, e);
            }
            else if (drag != null && e.type == EventType.MouseDrag)
            {
                Vector3 world;
                if (TryWorld(e.mousePosition, out world)) drag.MoveTo(world);
                e.Use();
            }
            else if (drag != null && e.type == EventType.MouseUp && e.button == 0)
            {
                Vector3 world;
                bool ok = TryWorld(e.mousePosition, out world);
                Drop(ok, world);
                e.Use();
            }
        }

        private void TryBeginDrag(Vector3 world, Event e)
        {
            // Ship slots first.
            for (int v = 0; v < ShipView.Active.Count; v++)
            {
                ShipView view = ShipView.Active[v];
                Ship ship = view.CurrentShip;
                if (ship == null) continue;
                int i = view.SlotAt(world);
                if (i < 0) continue;
                PartType type = PartTypes.All[i];
                if (ship.GetPart(type) == null) continue;
                Begin(PartSlot.OnShip(ship, type), view.slotAreas[i].transform, world);
                e.Use();
                return;
            }

            // Then inventory slots.
            int count = slots == null ? 0 : slots.Length;
            for (int i = 0; i < count; i++)
            {
                InventorySlotRefs s = slots[i];
                if (s == null || !DragVisual.Hits(s.area, world)) continue;
                if (moves.Inventory.GetPart(i) == null) continue;
                Transform handle = s.dragRoot != null ? s.dragRoot : s.area.transform;
                Begin(PartSlot.InInventory(i), handle, world);
                e.Use();
                return;
            }
        }

        private void Begin(PartSlot from, Transform handle, Vector3 world)
        {
            dragFrom = from;
            drag = new DragVisual(handle, world);
        }

        private void Drop(bool pointerKnown, Vector3 world)
        {
            PartSlot from = dragFrom;
            // The dragged sprite goes home first. If the move works, the views
            // redraw from the events. If it is refused, it is already back.
            CancelDrag();
            if (!pointerKnown || from == null) return;

            if (DragVisual.Hits(binArea, world))
            {
                moves.Bin(from);
                return;
            }

            PartSlot to = FindTarget(world);
            if (to != null) moves.Move(from, to);
        }

        private PartSlot FindTarget(Vector3 world)
        {
            for (int v = 0; v < ShipView.Active.Count; v++)
            {
                ShipView view = ShipView.Active[v];
                Ship ship = view.CurrentShip;
                if (ship == null) continue;
                int i = view.SlotAt(world);
                if (i >= 0) return PartSlot.OnShip(ship, PartTypes.All[i]);
            }

            int count = slots == null ? 0 : slots.Length;
            for (int i = 0; i < count; i++)
            {
                InventorySlotRefs s = slots[i];
                if (s != null && DragVisual.Hits(s.area, world)) return PartSlot.InInventory(i);
            }

            if (DragVisual.Hits(panelArea, world)) return PartSlot.InInventoryAny();
            return null;
        }

        private void CancelDrag()
        {
            if (drag != null) drag.End();
            drag = null;
            dragFrom = null;
        }

        private static bool TryWorld(Vector2 guiPoint, out Vector3 world)
        {
            world = Vector3.zero;
            Camera cam = Camera.main;
            if (cam == null) return false;
            float depth = Mathf.Abs(cam.transform.position.z);
            world = cam.ScreenToWorldPoint(new Vector3(guiPoint.x, Screen.height - guiPoint.y, depth));
            world.z = 0f;
            return true;
        }
    }
}
