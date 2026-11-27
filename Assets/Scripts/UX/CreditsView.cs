using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// The credits counter. It shows the Run's credits and updates on every
    /// credits changed event, also when the total is below 0 during the day.
    /// The following of the run is in CreditsDisplay (Logic). This view only
    /// shows it. The text element (key "credits", {0} is the total) gets its
    /// words from ui_text.json through its TextKeyView.
    /// </summary>
    public sealed class CreditsView : MonoBehaviour
    {
        [Tooltip("Text element with key credits. This view sets the number.")]
        public TextKeyView creditsText;

        private readonly CreditsDisplay display = new CreditsDisplay();
        private bool hooked;

        /// <summary>The credits shown now.</summary>
        public int ShownCredits
        {
            get { return display.Shown; }
        }

        /// <summary>Connects to a run (or a new run) and shows its credits.</summary>
        public void Bind(Run newRun)
        {
            if (!hooked)
            {
                display.Changed += Show;
                hooked = true;
            }
            display.Bind(newRun);
        }

        private void OnDestroy()
        {
            display.Unbind();
            if (hooked) display.Changed -= Show;
            hooked = false;
        }

        private void Show(int total)
        {
            if (creditsText != null) creditsText.SetArgs(total);
        }
    }
}
