using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace GoodEnoughRepairs
{
    /// <summary>The message lines of the warning dialog, one text element each.</summary>
    public enum WarningLine
    {
        Mechanic,
        BuyPrice,
        BuyBinned,
        ReleasePayment,
        ReleasePenalty,
        ReleaseNet
    }

    /// <summary>
    /// Small helpers that make UI parts (canvas, image, button) and the event
    /// system. They live here, in the UX assembly, so the prefab builder does not
    /// need a reference to the UI package. No rules here.
    /// </summary>
    public static class UiParts
    {
        private static RectTransform ToRect(GameObject go, Vector3 pos, Vector3 scale)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.localPosition = pos;
            rt.localScale = scale;
            rt.sizeDelta = new Vector2(1f, 1f);
            return rt;
        }

        /// <summary>A world-space canvas with a raycaster, drawn at the sorting order.</summary>
        public static void MakeCanvas(GameObject go, int sortingOrder)
        {
            Vector3 pos = go.transform.localPosition;
            Vector3 scale = go.transform.localScale;
            Canvas canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            ToRect(go, pos, scale);
        }

        /// <summary>A see-through image that blocks clicks. Size: 1 unit times the object's scale.</summary>
        public static Image MakeImage(GameObject go)
        {
            Vector3 pos = go.transform.localPosition;
            Vector3 scale = go.transform.localScale;
            Image image = go.GetComponent<Image>();
            if (image == null) image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = true;
            ToRect(go, pos, scale);
            return image;
        }

        /// <summary>A see-through button. Size: 1 unit times the object's scale.</summary>
        public static void MakeButton(GameObject go)
        {
            Image image = MakeImage(go);
            Button button = go.GetComponent<Button>();
            if (button == null) button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
        }

        /// <summary>The Button on an object, or null.</summary>
        public static Component GetButton(GameObject go)
        {
            return go.GetComponent<Button>();
        }

        /// <summary>Adds an event system with the Input System UI module when the scene has none.</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }

    /// <summary>
    /// The warning dialog for buying a part and releasing a ship, on a prefab.
    /// One dialog is shared by all bays. It shows the numbers from the BuyRelease
    /// previews and holds no rules. Confirm calls BuyRelease.Buy or
    /// BuyRelease.Release; Cancel closes the dialog and changes nothing.
    ///
    /// Every release goes through RequestRelease (or OpenRelease): a ship with
    /// nothing empty or weak is released at once through the release command, any
    /// other ship opens the release warning first. There is no other release path.
    ///
    /// All words come from ui_text.json, shown in text elements on the prefab
    /// (TextKeyView). Only the lines that apply to the open warning are shown:
    /// buy: the mechanic's line (buy_warning), price, and the binned line when the
    /// old part would be binned; release: the mechanic's line (release_warning),
    /// payment, the penalty line when parts get fitted, and the net result. A line
    /// without a linked text element is not shown.
    ///
    /// Confirm and Cancel are UI buttons (clicked from code), and a click blocker
    /// is shown with the dialog so nothing behind it gets UI clicks. There is no
    /// OnGUI. If the ship leaves, or the slot is no longer buyable, the dialog
    /// closes by itself and nothing is bought.
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

        private const int LineCount = 6;

        [Header("Sprites")]
        [Tooltip("Child object that holds the dialog. It is hidden until a warning opens.")]
        public GameObject dialogRoot;

        [Tooltip("Dialog background.")]
        public SpriteRenderer panel;

        [Tooltip("Optional. The part of the dialog where the message is written.")]
        public SpriteRenderer textArea;

        [Tooltip("Confirm button sprite (the look).")]
        public SpriteRenderer confirmButton;

        [Tooltip("Cancel button sprite (the look).")]
        public SpriteRenderer cancelButton;

        [Header("Buttons and blocker")]
        [Tooltip("UI button for Confirm.")]
        public Button confirmUiButton;

        [Tooltip("UI button for Cancel.")]
        public Button cancelUiButton;

        [Tooltip("Object with a raycast-blocking image. Shown and hidden with the dialog.")]
        public GameObject clickBlocker;

        [Header("Text elements")]
        [Tooltip("Dialog title. This view sets its key (buy_title or release_title).")]
        public TextKeyView titleText;

        [Tooltip("Confirm label. This view sets its key (confirm_buy or confirm_release).")]
        public TextKeyView confirmText;

        [Tooltip("Cancel label. This view sets its key (cancel_buy or cancel_release).")]
        public TextKeyView cancelText;

        [Tooltip("The mechanic's line, buy and release. The view overwrites the text, so any textKey will do.")]
        public TextKeyView mechanicText;

        [Tooltip("Buy warning: key buy_price, {0} is the price.")]
        public TextKeyView buyPriceText;

        [Tooltip("Buy warning: key buy_binned, {0} is the scrap payment. Only when the old part is binned.")]
        public TextKeyView buyBinnedText;

        [Tooltip("Release warning: key release_payment, {0} is the payment.")]
        public TextKeyView releasePaymentText;

        [Tooltip("Release warning: key release_penalty, {0} parts fitted, {1} their cost. Only when parts are fitted.")]
        public TextKeyView releasePenaltyText;

        [Tooltip("Release warning: key release_net, {0} is the net result.")]
        public TextKeyView releaseNetText;

        private BuyRelease buyRelease;
        private ContentData content;
        private Kind kind = Kind.None;
        private Ship ship;
        private PartType partType;
        private BuyPreview buyPreview;
        private ReleasePreview releasePreview;
        private string mechanicLine = "";
        private readonly bool[] shown = new bool[LineCount];

        /// <summary>True while a warning is shown.</summary>
        public bool IsOpen
        {
            get { return kind != Kind.None; }
        }

        /// <summary>The mechanic's line of the open warning, or "" when there is none.</summary>
        public string MechanicLine
        {
            get { return mechanicLine; }
        }

        private ContentData Text
        {
            get { return content ?? TextKeyView.Shared; }
        }

        /// <summary>
        /// Whether a message line applies to a warning. Pure rule for which lines show.
        /// </summary>
        public static bool LineApplies(WarningLine line, bool isBuy, bool oldPartBinned, int penaltyFills,
            bool hasMechanicLine)
        {
            switch (line)
            {
                case WarningLine.BuyPrice: return isBuy;
                case WarningLine.BuyBinned: return isBuy && oldPartBinned;
                case WarningLine.Mechanic: return hasMechanicLine;
                case WarningLine.ReleasePayment: return !isBuy;
                case WarningLine.ReleasePenalty: return !isBuy && penaltyFills > 0;
                case WarningLine.ReleaseNet: return !isBuy;
                default: return false;
            }
        }

        /// <summary>True when a line is shown for the open warning (false when closed).</summary>
        public bool IsLineShown(WarningLine line)
        {
            return shown[(int)line];
        }

        private void Awake()
        {
            if (confirmUiButton != null)
            {
                confirmUiButton.onClick.RemoveListener(Confirm);
                confirmUiButton.onClick.AddListener(Confirm);
            }
            if (cancelUiButton != null)
            {
                cancelUiButton.onClick.RemoveListener(Cancel);
                cancelUiButton.onClick.AddListener(Cancel);
            }
            HideDialog();
        }

        private void OnDestroy()
        {
            if (confirmUiButton != null) confirmUiButton.onClick.RemoveListener(Confirm);
            if (cancelUiButton != null) cancelUiButton.onClick.RemoveListener(Cancel);
        }

        /// <summary>Connects the dialog to the commands and the text and mechanic's lines.</summary>
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
        /// The only way a ship is released from the views. A ship with no empty or
        /// weak slot is released at once through the release command (no warning is
        /// needed). Otherwise the release warning opens, and only Confirm releases.
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
            mechanicLine = PickLine(MomentKeys.BuyWarning);
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
            mechanicLine = PickLine(MomentKeys.ReleaseWarning);
            SetOpen(Kind.Release);
            return true;
        }

        // A mechanic's line from content, picked once when the warning opens.
        private string PickLine(string moment)
        {
            ContentData t = Text;
            return t == null ? "" : (t.GetLine(moment) ?? "");
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
            mechanicLine = "";
            for (int i = 0; i < shown.Length; i++) shown[i] = false;
            HideDialog();
        }

        private void HideDialog()
        {
            if (dialogRoot != null) dialogRoot.SetActive(false);
            if (clickBlocker != null) clickBlocker.SetActive(false);
        }

        private void SetOpen(Kind k)
        {
            kind = k;
            if (titleText != null) titleText.SetKey(TitleKey());
            if (confirmText != null) confirmText.SetKey(ConfirmKey());
            if (cancelText != null) cancelText.SetKey(CancelKey());
            RefreshLines();
            if (clickBlocker != null) clickBlocker.SetActive(true);
            if (dialogRoot != null) dialogRoot.SetActive(true);
        }

        private string TitleKey()
        {
            return kind == Kind.Buy ? UiKeys.BuyTitle : UiKeys.ReleaseTitle;
        }

        private string ConfirmKey()
        {
            return kind == Kind.Buy ? UiKeys.ConfirmBuy : UiKeys.ConfirmRelease;
        }

        private string CancelKey()
        {
            return kind == Kind.Buy ? UiKeys.CancelBuy : UiKeys.CancelRelease;
        }

        // Shows the lines that apply and puts the preview's numbers into them.
        private void RefreshLines()
        {
            bool isBuy = kind == Kind.Buy;
            bool binned = isBuy && buyPreview != null && buyPreview.OldPartBinned;
            int fills = !isBuy && releasePreview != null ? releasePreview.PenaltyFills : 0;
            bool hasMechanic = !string.IsNullOrEmpty(mechanicLine);

            for (int i = 0; i < LineCount; i++)
            {
                shown[i] = IsOpen && LineApplies((WarningLine)i, isBuy, binned, fills, hasMechanic);
            }

            if (mechanicText != null)
            {
                mechanicText.gameObject.SetActive(shown[(int)WarningLine.Mechanic]);
                if (shown[(int)WarningLine.Mechanic] && mechanicText.textMesh != null)
                {
                    mechanicText.textMesh.text = mechanicLine;
                }
            }

            bool hasBuy = isBuy && buyPreview != null;
            bool hasRelease = !isBuy && IsOpen && releasePreview != null;

            ApplyLine(buyPriceText, shown[(int)WarningLine.BuyPrice] && hasBuy, UiKeys.BuyPrice);
            if (shown[(int)WarningLine.BuyPrice] && hasBuy && buyPriceText != null)
            {
                buyPriceText.SetArgs(buyPreview.Price);
            }

            ApplyLine(buyBinnedText, shown[(int)WarningLine.BuyBinned] && hasBuy, UiKeys.BuyBinned);
            if (shown[(int)WarningLine.BuyBinned] && hasBuy && buyBinnedText != null)
            {
                buyBinnedText.SetArgs(buyPreview.ScrapAmount);
            }

            ApplyLine(releasePaymentText, shown[(int)WarningLine.ReleasePayment] && hasRelease,
                UiKeys.ReleasePayment);
            if (shown[(int)WarningLine.ReleasePayment] && hasRelease && releasePaymentText != null)
            {
                releasePaymentText.SetArgs(releasePreview.Payment);
            }

            ApplyLine(releasePenaltyText, shown[(int)WarningLine.ReleasePenalty] && hasRelease,
                UiKeys.ReleasePenalty);
            if (shown[(int)WarningLine.ReleasePenalty] && hasRelease && releasePenaltyText != null)
            {
                releasePenaltyText.SetArgs(releasePreview.PenaltyFills, releasePreview.PenaltyTotal);
            }

            ApplyLine(releaseNetText, shown[(int)WarningLine.ReleaseNet] && hasRelease, UiKeys.ReleaseNet);
            if (shown[(int)WarningLine.ReleaseNet] && hasRelease && releaseNetText != null)
            {
                releaseNetText.SetArgs(releasePreview.NetResult);
            }
        }

        private static void ApplyLine(TextKeyView view, bool show, string key)
        {
            if (view == null) return;
            view.gameObject.SetActive(show);
            if (show) view.SetKey(key);
        }

        // Keeps the numbers current, and closes when the warning is no longer valid.
        private void Update()
        {
            if (!IsOpen || buyRelease == null) return;
            if (kind == Kind.Buy)
            {
                buyPreview = buyRelease.PreviewBuy(ship, partType);
                if (!buyPreview.CanBuy)
                {
                    Close();
                    return;
                }
            }
            else
            {
                releasePreview = buyRelease.PreviewRelease(ship);
                if (!releasePreview.CanRelease)
                {
                    Close();
                    return;
                }
            }
            RefreshLines();
        }

        private void OnDisable()
        {
            Close();
        }
    }
}
