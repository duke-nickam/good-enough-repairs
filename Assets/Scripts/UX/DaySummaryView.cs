using System;
using UnityEngine;
using UnityEngine.UI;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// The end-of-day panel. It shows Earned, Spent, Upkeep and Balance, with a
    /// Continue button. When the run is over (bankrupt or the last day) it shows
    /// the run-over title and a Play again button instead. The state is in
    /// SummaryPanelState (Logic); this view only shows it and forwards the
    /// buttons. All words come from ui_text.json through the text elements
    /// (TextKeyView); this view only sets keys and numbers.
    /// </summary>
    public sealed class DaySummaryView : MonoBehaviour
    {
        [Tooltip("Holds all the panel objects. Hidden until a day ends. Must not be the object with this view.")]
        public GameObject panelRoot;

        [Tooltip("Optional. Shown and hidden with the panel, blocks clicks behind it.")]
        public GameObject clickBlocker;

        public TextKeyView titleText;
        [Tooltip("Key summary_earned, {0} is the number.")]
        public TextKeyView earnedText;
        [Tooltip("Key summary_spent, {0} is the number.")]
        public TextKeyView spentText;
        [Tooltip("Key summary_upkeep, {0} is the number.")]
        public TextKeyView upkeepText;
        [Tooltip("Key summary_balance, {0} is the number.")]
        public TextKeyView balanceText;
        [Tooltip("Key summary_note. Hidden when the run is over.")]
        public TextKeyView noteText;

        [Tooltip("Continue button (label key summary_continue). Hidden when the run is over.")]
        public Button continueButton;
        [Tooltip("Play again button (label key run_end_play_again). Shown only when the run is over.")]
        public Button playAgainButton;

        private readonly SummaryPanelState state = new SummaryPanelState();
        private bool hooked;

        /// <summary>True while the panel is open.</summary>
        public bool IsOpen { get { return state.IsOpen; } }
        /// <summary>True when the open panel shows the Continue button.</summary>
        public bool ShowsContinue { get { return state.ShowsContinue; } }
        /// <summary>True when the open panel shows the Play again button.</summary>
        public bool ShowsPlayAgain { get { return state.ShowsPlayAgain; } }
        /// <summary>True when the panel says the run is over.</summary>
        public bool IsRunOver { get { return state.IsRunOver; } }
        /// <summary>Key of the title shown now.</summary>
        public string TitleKey { get { return state.TitleKey; } }
        /// <summary>The summary shown, or null.</summary>
        public DaySummary Shown { get { return state.Shown; } }

        /// <summary>
        /// Connects to a run. continued is called after Continue moved the run on;
        /// playAgain is called when Play again is pressed.
        /// </summary>
        public void Bind(Run newRun, Action continued, Action playAgain)
        {
            if (!hooked)
            {
                state.Changed += Apply;
                hooked = true;
            }
            state.Bind(newRun, continued, playAgain);

            if (continueButton != null)
            {
                continueButton.onClick.RemoveListener(PressContinue);
                continueButton.onClick.AddListener(PressContinue);
            }
            if (playAgainButton != null)
            {
                playAgainButton.onClick.RemoveListener(PressPlayAgain);
                playAgainButton.onClick.AddListener(PressPlayAgain);
            }
        }

        private void OnDestroy()
        {
            state.Unbind();
            if (hooked) state.Changed -= Apply;
            hooked = false;
        }

        private void Apply()
        {
            bool open = state.IsOpen;
            if (panelRoot != null && panelRoot.activeSelf != open) panelRoot.SetActive(open);
            if (clickBlocker != null && clickBlocker.activeSelf != open) clickBlocker.SetActive(open);
            if (continueButton != null) continueButton.gameObject.SetActive(open && state.ShowsContinue);
            if (playAgainButton != null) playAgainButton.gameObject.SetActive(open && state.ShowsPlayAgain);
            if (noteText != null) noteText.gameObject.SetActive(open && !state.IsRunOver);

            DaySummary shown = state.Shown;
            if (!open || shown == null) return;
            if (titleText != null) titleText.SetKey(state.TitleKey);
            if (earnedText != null) earnedText.SetArgs(shown.Earned);
            if (spentText != null) spentText.SetArgs(shown.Spent);
            if (upkeepText != null) upkeepText.SetArgs(shown.Upkeep);
            if (balanceText != null) balanceText.SetArgs(shown.Balance);
        }

        /// <summary>The Continue button.</summary>
        public void PressContinue()
        {
            state.PressContinue();
        }

        /// <summary>The Play again button.</summary>
        public void PressPlayAgain()
        {
            state.PressPlayAgain();
        }
    }
}
