using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Match3.Achievements
{
    /// <summary>
    /// The Achievements button in the side panel and the window it opens: a scrolling list of
    /// every achievement with the player's progress. A guest sees the same list, all locked,
    /// under "Sign in to earn achievements".
    ///
    /// It only displays what the <see cref="AchievementsClient"/> has, and asks it to load
    /// again each time the window opens.
    ///
    /// Like the other menus, the GameManager only allows it between rounds, by calling
    /// <see cref="SetAvailable"/>.
    /// </summary>
    public class AchievementsMenu : MonoBehaviour
    {
        [SerializeField] private AchievementsClient client;
        [Tooltip("Which sprite to show for each icon name.")]
        [SerializeField] private AchievementIcons icons;

        [Header("Side panel")]
        [Tooltip("The Achievements button in the side panel that opens the window.")]
        [SerializeField] private Button openButton;

        [Header("Window")]
        [Tooltip("The full-screen modal (dimmed backdrop + window).")]
        [SerializeField] private GameObject panel;
        [Tooltip("\"3 of 11 unlocked\", \"Sign in to earn achievements\", or why the list couldn't be loaded.")]
        [SerializeField] private TMP_Text statusText;
        [Tooltip("The scrolling list's content: rows are added under it.")]
        [SerializeField] private RectTransform listContent;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private AchievementRow rowPrefab;
        [SerializeField] private Button closeButton;

        // The rows made so far. They're reused from one refresh to the next (and hidden if
        // there are too many), which is cheaper than destroying and re-making them.
        private readonly List<AchievementRow> rows = new List<AchievementRow>();
        private InputAction cancelAction;

        public bool IsOpen => panel != null && panel.activeSelf;

        private void Awake()
        {
            if (client == null || icons == null || openButton == null || panel == null || statusText == null ||
                listContent == null || scroll == null || rowPrefab == null || closeButton == null)
            {
                Debug.LogError("AchievementsMenu: assign every field in the Inspector.", this);
                enabled = false;
                return;
            }

            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            client.Changed += Refresh;

            cancelAction = InputSystem.actions.FindAction("UI/Cancel", throwIfNotFound: true);

            panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (openButton != null)
            {
                openButton.onClick.RemoveListener(Open);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
            if (client != null)
            {
                client.Changed -= Refresh;
            }
        }

        private void Update()
        {
            if (IsOpen && cancelAction.WasPressedThisFrame())
            {
                Close();
            }
        }

        /// <summary>Shows the Achievements button when true. When false (a round is running) it's hidden and the window closed.</summary>
        public void SetAvailable(bool available)
        {
            if (!enabled)
            {
                return;
            }
            openButton.gameObject.SetActive(available);
            if (!available)
            {
                Close();
            }
        }

        private void Open()
        {
            panel.SetActive(true);
            // Back to the top of the list. (1 is the top: the value is how far up we are.)
            scroll.verticalNormalizedPosition = 1f;
            Refresh();
            // Progress may have changed since the list was last loaded (on another device, say).
            client.Refresh();
            Select(closeButton);
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            panel.SetActive(false);
            Select(openButton.gameObject.activeInHierarchy ? openButton : null);
        }

        /// <summary>Makes the window match the client. Does nothing while the window is closed.</summary>
        private void Refresh()
        {
            if (!IsOpen)
            {
                return;
            }

            Achievement[] list = client.Items ?? new Achievement[0];
            int unlocked = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (i == rows.Count)
                {
                    // Instantiate copies the prefab into the scene, as a child of listContent.
                    // Its Vertical Layout Group then places it under the rows before it.
                    rows.Add(Instantiate(rowPrefab, listContent));
                }
                rows[i].gameObject.SetActive(true);
                rows[i].Show(list[i], icons.Get(list[i].icon));
                if (list[i].IsUnlocked)
                {
                    unlocked++;
                }
            }
            for (int i = list.Length; i < rows.Count; i++)
            {
                rows[i].gameObject.SetActive(false);
            }

            statusText.text = StatusLine(client, unlocked);
        }

        private static string StatusLine(AchievementsClient client, int unlocked)
        {
            if (client.Message != null)
            {
                return client.Message;
            }
            if (client.Items == null)
            {
                return client.Loading ? "Loading..." : "";
            }
            if (!client.IsSignedIn)
            {
                return "Sign in to earn achievements";
            }
            return $"{unlocked} of {client.Items.Length} unlocked";
        }

        /// <summary>
        /// Gives keyboard/gamepad focus to <paramref name="selectable"/> (null = nothing), one
        /// frame from now, so the key press that got us here can't also press it. (The same as
        /// in LeaderboardMenu, which explains it in more detail.)
        /// </summary>
        private void Select(Selectable selectable)
        {
            if (EventSystem.current == null)
            {
                return;
            }
            EventSystem.current.SetSelectedGameObject(null);
            if (selectable != null)
            {
                StartCoroutine(SelectNextFrame(selectable));
            }
        }

        private static IEnumerator SelectNextFrame(Selectable selectable)
        {
            yield return null;
            if (selectable != null && selectable.gameObject.activeInHierarchy && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
            }
        }
    }
}
