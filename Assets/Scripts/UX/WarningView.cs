using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// The warning dialog for buying a part and releasing a ship, on a prefab.
    /// One dialog is shared by all bays. It shows the numbers from the BuyRelease
    /// previews and holds no rules. Confirm calls BuyRelease.Buy or
    /// BuyRelease.Release; Cancel closes the dialog and changes nothing.
    ///
    /// While it is open it uses every mouse event, so nothing else can be clicked.
    /// If the ship leaves, or the slot is no longer buyable, it closes by itself
    /// and nothing is bought.
    ///
    /// The sprites are on the prefab (dialogRoot is the object that is shown and
    /// hidden). The text is drawn with OnGUI over the text area sprite.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class WarningView : MonoBehaviour
    {
        private enum Kind
        {
            None,
            Buy,
            Release
        }

        [Header("Sprites")]
        [Tooltip("Child object that holds the dialog sprites. It is hidden until a warning opens.")]
        public GameObject dialogRoot;

        [Tooltip("Dialog background. Used as the text area when textArea is empty.")]
        public SpriteRenderer panel;

        [Tooltip("Optional. The part of the dialog where the message is written.")]
        public SpriteRenderer textArea;

        [Tooltip("Confirm button. Its bounds are the click area.")]
        public SpriteRenderer confirmButton;

        [Tooltip("Cancel button. Its bounds are the click area.")]
        public SpriteRenderer cancelButton;

        [Header("Text templates")]
        public string buyTitle = "Buy a new part?";
        [Tooltip("{0} is the price of the new part.")]
        public string buyPriceText = "Price: {0}";
        [Tooltip("{0} is the scrap paid for the binned old part.")]
        public string buyBinnedText = "Inventory is full: the old part will be binned (scrap {0}).";
        public string releaseTitle = "Release this ship?";
        [Tooltip("{0} is the payment.")]
        public string releasePaymentText = "Payment: {0}";
        [Tooltip("{0} is the number of slots filled, {1} the price of one, {2} the total.")]
        public string releasePenaltyText = "{0} slot(s) filled at {1} each: {2}";
        [Tooltip("{0} is payment minus penalty parts.")]
        public string releaseNetText = "Net result: {0}";
        public string confirmLabel = "Confirm";
        public string cancelLabel = "Cancel";

        private BuyRelease buyRelease;
        private ContentData content;
        private Kind kind = Kind.None;
        private Ship ship;
        private PartType partType;
        private BuyPreview buyPreview;
        private ReleasePreview releasePreview;
        private string mechanicLine = "";
        private GUIStyle textStyle;
        private GUIStyle buttonStyle;

        /// <summary>True while a warning is shown.</summary>
        public bool IsOpen
        {
            get { return kind != Kind.None; }
        }

        private void Awake()
        {
            if (dialogRoot != null) dialogRoot.SetActive(false);
        }

        /// <summary>Connects the dialog to the commands and the mechanic's lines.</summary>
        public void Bind(BuyRelease commands, ContentData contentData)
        {
            Close();
            buyRelease = commands;
            content = contentData;
        }

        /// <summary>Opens the buy warning if the slot can be bought. Returns whether it opened.</summary>
        public bool RequestBuy(Ship target, PartType type)
        {
            return OpenBuy(target, type);
        }

        /// <summary>
        /// Releases at once when no slot is empty or weak (no warning is needed).
        /// Otherwise opens the release warning.
        /// </summary>
        public void RequestRelease(Ship target)
        {
            if (buyRelease == null || IsOpen) return;
            ReleasePreview p = buyRelease.PreviewRelease(target);
            if (!p.CanRelease) return;
            if (p.PenaltyFills == 0)
            {
                buyRelease.Release(target);
                return;
            }
            OpenRelease(target);
        }

        /// <summary>Opens the buy warning for a slot. False if the preview says it cannot be bought.</summary>
        public bool OpenBuy(Ship target, PartType type)
        {
            if (buyRelease == null || IsOpen) return false;
            BuyPreview p = buyRelease.PreviewBuy(target, type);
            if (!p.CanBuy) return false;
            ship = target;
            partType = type;
            buyPreview = p;
            mechanicLine = "";
            SetOpen(Kind.Buy);
            return true;
        }

        /// <summary>Opens the release warning, whatever the numbers are. False if release is not possible.</summary>
        public bool OpenRelease(Ship target)
        {
            if (buyRelease == null || IsOpen) return false;
            ReleasePreview p = buyRelease.PreviewRelease(target);
            if (!p.CanRelease) return false;
            ship = target;
            releasePreview = p;
            mechanicLine = content == null ? "" : content.GetLine(MomentKeys.ReleaseWarning);
            SetOpen(Kind.Release);
            return true;
        }

        /// <summary>Runs the command the warning is about, then closes. Does nothing when closed.</summary>
        public void Confirm()
        {
            if (!IsOpen || buyRelease == null) return;
            Kind k = kind;
            Ship s = ship;
            PartType t = partType;
            Close();
            if (k == Kind.Buy) buyRelease.Buy(s, t);
            else if (k == Kind.Release) buyRelease.Release(s);
        }

        /// <summary>Closes the dialog. Nothing is changed.</summary>
        public void Cancel()
        {
            Close();
        }

        /// <summary>Closes the dialog.</summary>
        public void Close()
        {
            kind = Kind.None;
            ship = null;
            buyPreview = null;
            releasePreview = null;
            if (dialogRoot != null) dialogRoot.SetActive(false);
        }

        private void SetOpen(Kind k)
        {
            kind = k;
            if (dialogRoot != null) dialogRoot.SetActive(true);
        }

        // Keeps the numbers current, and closes when the warning is no longer valid.
        private void Update()
        {
            if (!IsOpen || buyRelease == null) return;
            if (kind == Kind.Buy)
            {
                buyPreview = buyRelease.PreviewBuy(ship, partType);
                if (!buyPreview.CanBuy) Close();
            }
            else
            {
                releasePreview = buyRelease.PreviewRelease(ship);
                if (!releasePreview.CanRelease) Close();
            }
        }

        private void OnDisable()
        {
            Close();
        }

        private void OnGUI()
        {
            Event e = Event.current;
            if (e == null || !IsOpen) return;

            if (e.type == EventType.Repaint)
            {
                Draw();
                return;
            }

            if (e.type != EventType.MouseDown && e.type != EventType.MouseUp && e.type != EventType.MouseDrag)
            {
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                Vector3 world;
                if (SpriteScreen.TryWorld(e.mousePosition, out world))
                {
                    if (DragVisual.Hits(confirmButton, world)) Confirm();
                    else if (DragVisual.Hits(cancelButton, world)) Cancel();
                }
            }
            e.Use();
        }

        private void Draw()
        {
            if (textStyle == null)
            {
                textStyle = new GUIStyle(GUI.skin.label);
                textStyle.wordWrap = true;
                textStyle.alignment = TextAnchor.UpperLeft;
                buttonStyle = new GUIStyle(GUI.skin.label);
                buttonStyle.alignment = TextAnchor.MiddleCenter;
            }

            Rect r;
            SpriteRenderer area = textArea != null ? textArea : panel;
            if (SpriteScreen.TryRect(area, out r))
            {
                const float pad = 8f;
                GUI.Label(new Rect(r.x + pad, r.y + pad, r.width - 2f * pad, r.height - 2f * pad),
                    Message(), textStyle);
            }
            if (SpriteScreen.TryRect(confirmButton, out r)) GUI.Label(r, confirmLabel, buttonStyle);
            if (SpriteScreen.TryRect(cancelButton, out r)) GUI.Label(r, cancelLabel, buttonStyle);
        }

        // Only puts the preview's numbers into the text templates.
        private string Message()
        {
            if (kind == Kind.Buy && buyPreview != null)
            {
                string text = buyTitle + "\n" + string.Format(buyPriceText, buyPreview.Price);
                if (buyPreview.OldPartBinned) text += "\n" + string.Format(buyBinnedText, buyPreview.ScrapAmount);
                return text;
            }
            if (kind == Kind.Release && releasePreview != null)
            {
                string text = "";
                if (!string.IsNullOrEmpty(mechanicLine)) text += mechanicLine + "\n";
                text += releaseTitle;
                text += "\n" + string.Format(releasePaymentText, releasePreview.Payment);
                text += "\n" + string.Format(releasePenaltyText, releasePreview.PenaltyFills,
                    releasePreview.PenaltyPrice, releasePreview.PenaltyTotal);
                text += "\n" + string.Format(releaseNetText, releasePreview.NetResult);
                return text;
            }
            return "";
        }
    }
}
