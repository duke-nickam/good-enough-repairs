using System.Collections.Generic;
using UnityEngine;

namespace GoodEnoughRepairs
{
    /// <summary>
    /// A text element in a prefab whose words come from ui_text.json. It holds a
    /// text key and fills the TextMesh on the same object at start and whenever the
    /// key, the numbers or the shared content change. The builder tool adds it for
    /// a spec object with "textKey". It holds no rules and no player text.
    /// </summary>
    public sealed class TextKeyView : MonoBehaviour
    {
        private static readonly List<TextKeyView> active = new List<TextKeyView>();
        private static ContentData shared;

        [Tooltip("Key in ui_text.json.")]
        public string textKey = "";

        [Tooltip("The text element to fill. Empty means the TextMesh on this object.")]
        public TextMesh textMesh;

        private object[] args = new object[0];

        /// <summary>The content all text elements read. Loaded on first use if nobody set it.</summary>
        public static ContentData Shared
        {
            get
            {
                if (shared == null) shared = ContentLoader.Load();
                return shared;
            }
        }

        /// <summary>Sets the shared content and redraws every text element on screen.</summary>
        public static void SetShared(ContentData content)
        {
            shared = content;
            for (int i = 0; i < active.Count; i++) active[i].Refresh();
        }

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        private void Start()
        {
            Refresh();
        }

        /// <summary>Changes the key and redraws.</summary>
        public void SetKey(string key)
        {
            string k = key ?? "";
            if (k == textKey) return;
            textKey = k;
            Refresh();
        }

        /// <summary>Sets the numbers for {0}, {1}. Redraws only when they changed.</summary>
        public void SetArgs(params object[] values)
        {
            object[] next = values ?? new object[0];
            if (SameArgs(next)) return;
            args = next;
            Refresh();
        }

        /// <summary>Fills the text element from the key and the numbers.</summary>
        public void Refresh()
        {
            if (textMesh == null) textMesh = GetComponent<TextMesh>();
            if (textMesh == null) return;
            string text = Shared.Text(textKey, args);
            if (textMesh.text != text) textMesh.text = text;
        }

        private bool SameArgs(object[] next)
        {
            if (next.Length != args.Length) return false;
            for (int i = 0; i < next.Length; i++)
            {
                if (!Equals(next[i], args[i])) return false;
            }
            return true;
        }
    }
}
