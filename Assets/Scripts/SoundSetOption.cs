using UnityEngine;
using UnityEngine.UI;

namespace Match3
{
    /// <summary>
    /// A Settings option for one <see cref="Match3.SoundSet"/>: its name plus a small play
    /// button that lets the player hear the set without choosing it.
    /// </summary>
    public class SoundSetOption : SettingsOption
    {
        // This Button sits inside the option, which is itself a Button. That works because a
        // click goes to the topmost graphic under the pointer and then to the nearest object
        // above it that handles clicks: clicking the play button reaches this Button and stops
        // there, so it doesn't also count as a click on the option around it.
        [Tooltip("The small play button. Clicking it previews the set instead of selecting it.")]
        [SerializeField] private Button previewButton;

        public SoundSet SoundSet { get; private set; }

        /// <summary>Fired when the player clicks the play button.</summary>
        public event System.Action PreviewClicked;

        protected override void Awake()
        {
            base.Awake();
            if (previewButton == null)
            {
                Debug.LogError("SoundSetOption: assign the Preview Button.", this);
                return;
            }
            previewButton.onClick.AddListener(HandlePreviewClick);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (previewButton != null)
            {
                previewButton.onClick.RemoveListener(HandlePreviewClick);
            }
        }

        private void HandlePreviewClick()
        {
            PreviewClicked?.Invoke();
        }

        /// <summary>Fills in this option for <paramref name="set"/>.</summary>
        public void Setup(SoundSet set)
        {
            SoundSet = set;
            SetLabel(set.DisplayName);
        }
    }
}
