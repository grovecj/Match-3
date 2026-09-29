using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// One clickable choice in the Settings menu: a button with a name label and a highlight
    /// that shows when it's the current selection. <see cref="TokenStyleOption"/> and
    /// <see cref="BoardThemeOption"/> build on this and add their own preview.
    ///
    /// The option doesn't decide whether it's selected. The <see cref="SettingsMenu"/> asks the
    /// ThemeManager what's in use and calls <see cref="SetSelected"/>, so the highlight can
    /// never disagree with what the board actually shows.
    /// </summary>
    public abstract class SettingsOption : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;
        [Tooltip("Shown only while this option is the current selection.")]
        [SerializeField] private GameObject selectedHighlight;

        /// <summary>Fired when the player clicks this option.</summary>
        public event System.Action Clicked;

        protected virtual void Awake()
        {
            if (button == null || label == null || selectedHighlight == null)
            {
                Debug.LogError($"{GetType().Name}: assign the Button, Label and Selected Highlight.", this);
                return;
            }
            button.onClick.AddListener(HandleClick);
        }

        protected virtual void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }
        }

        private void HandleClick()
        {
            Clicked?.Invoke();
        }

        /// <summary>Sets the text shown under/next to the preview.</summary>
        protected void SetLabel(string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        /// <summary>Shows or hides the "this is the current choice" highlight.</summary>
        public void SetSelected(bool selected)
        {
            if (selectedHighlight != null)
            {
                selectedHighlight.SetActive(selected);
            }
        }
    }
}
