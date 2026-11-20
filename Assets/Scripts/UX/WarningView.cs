using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// The warning dialog for buying a part and releasing a ship, on a prefab.
    /// One dialog is shared by all bays. It shows the numbers from the BuyRelease
    /// previews and holds no rules. Confirm calls BuyRelease.Buy or
    /// BuyRelease.Release; Cancel closes the dialog and changes nothing.
    ///
    /// All words come from ui_text.json. The title and the two button labels can be
    /// text elements on the prefab (TextKeyView); this view sets their keys when a
    /// warning opens. Without them they are drawn with OnGUI.
    ///
    /// While it is open it uses every mouse event, so nothing else can be clicked.
    /// If the ship leaves, or the slot is no longer buyable, it closes by itself
    /// and nothing is bought.
    ///
    /// The sprites are on the prefab (dialogRoot is the object that is shown and
    /// hidden). The message is drawn with OnGUI over the text area sprite.
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

        [Header("Text elements (optional)")]
        [Tooltip("Dialog title. This view sets its key (buy_title or release_title).")]
        public TextKeyView titleText;

        [Tooltip("Confirm label. This view sets its key (confirm_buy or confirm_release).")]
        public TextKeyView confirmText;

        [Tooltip("Cancel label. This view sets its key (cancel_buy or cancel_release).")]
        public TextKeyView cancelText;

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

        private ContentData Text
        {
            get { return content ?? TextKeyView.Shared; }
        }

        private void Awake()
        {
            if (dialogRoot != null) dialogRoot.SetActive(false);
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
            if (titleText != null) titleText.SetKey(TitleKey());
            if (confirmText != null) confirmText.SetKey(ConfirmKey());
            if (cancelText != null) cancelText.SetKey(CancelKey());
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
            if (confirmText == null && SpriteScreen.TryRect(confirmButton, out r))
            {
                GUI.Label(r, Text.Text(ConfirmKey()), buttonStyle);
            }
            if (cancelText == null && SpriteScreen.TryRect(cancelButton, out r))
            {
                GUI.Label(r, Text.Text(CancelKey()), buttonStyle);
            }
        }

        private static string AddLine(string text, string line)
        {
            if (string.IsNullOrEmpty(line)) return text;
            return text.Length == 0 ? line : text + "\n" + line;
        }

        // Only puts the preview's numbers into the texts from ui_text.json.
        private string Message()
        {
            ContentData t = Text;
            string text = "";
            if (kind == Kind.Buy && buyPreview != null)
            {
                if (titleText == null) text = AddLine(text, t.Text(UiKeys.BuyTitle));
                text = AddLine(text, t.Text(UiKeys.BuyPrice, buyPreview.Price));
                if (buyPreview.OldPartBinned) text = AddLine(text, t.Text(UiKeys.BuyBinned, buyPreview.ScrapAmount));
                return text;
            }
            if (kind == Kind.Release && releasePreview != null)
            {
                text = AddLine(text, mechanicLine);
                if (titleText == null) text = AddLine(text, t.Text(UiKeys.ReleaseTitle));
                text = AddLine(text, t.Text(UiKeys.ReleasePayment, releasePreview.Payment));
                if (releasePreview.PenaltyFills > 0)
                {
                    text = AddLine(text, t.Text(UiKeys.ReleasePenalty, releasePreview.PenaltyFills,
                        releasePreview.PenaltyTotal));
                }
                text = AddLine(text, t.Text(UiKeys.ReleaseNet, releasePreview.NetResult));
                return text;
            }
            return "";
        }
    }
}
