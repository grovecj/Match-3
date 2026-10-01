using Match3;
using Match3.Accounts;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static UIBuilder;

/// <summary>
/// Editor-only helper that builds the account UI (issue #68): the Account button in the side
/// panel (above Leaderboard and the gear) and the modal Account window, plus an AccountManager
/// object if the scene doesn't have one yet, with every Inspector reference wired up
/// (including the GameManager's). Run it from the menu bar: Match3 > Build Account Menu, then
/// save the scene.
///
/// Like the other builders, running it again replaces the UI it built before. An existing
/// AccountManager is kept as it is.
/// </summary>
public static class AccountMenuBuilder
{
    private static readonly Color MessageColor = new Color(1f, 0.75f, 0.45f);

    [MenuItem("Match3/Build Account Menu")]
    public static void Build()
    {
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var gameManager = Object.FindAnyObjectByType<GameManager>();
        Transform sidePanel = canvas != null ? canvas.transform.Find("ScorePanel") : null;
        if (canvas == null || gameManager == null || sidePanel == null)
        {
            Debug.LogError("Build Account Menu: open the Game scene first (needs Canvas/ScorePanel and GameManager).");
            return;
        }

        // Remove what a previous run built, so running the tool twice doesn't duplicate it.
        DestroyChild(canvas.transform, "Account");
        DestroyChild(sidePanel, "AccountButton");

        AccountManager accountManager = FindOrCreateAccountManager();
        Button openButton = BuildOpenButton(sidePanel, out TMP_Text openButtonLabel);

        // Root: always active, holds the AccountMenu script. Last child of the Canvas, so its
        // window draws on top of everything else.
        GameObject root = NewUI("Account", canvas.transform);
        Stretch(Rect(root), 0);
        root.transform.SetAsLastSibling();
        var menu = root.AddComponent<AccountMenu>();

        BuildWindow(root, menu);
        SetReference(menu, "accountManager", accountManager);
        SetReference(menu, "openButton", openButton);
        SetReference(menu, "openButtonLabel", openButtonLabel);
        SetReference(gameManager, "accountMenu", menu);

        Undo.RegisterCreatedObjectUndo(root, "Build Account Menu");
        // Tell Unity the scene has unsaved changes (the * in the Hierarchy) so Ctrl+S saves them.
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = root;
        Debug.Log("Account menu built. Save the scene (Ctrl+S) to keep it.");
    }

    /// <summary>The scene's AccountManager, or a new one on its own GameObject.</summary>
    private static AccountManager FindOrCreateAccountManager()
    {
        var existing = Object.FindAnyObjectByType<AccountManager>();
        if (existing != null)
        {
            return existing;
        }
        // Not UI, so a plain GameObject at the top of the scene, like the GameManager.
        var go = new GameObject("AccountManager");
        Undo.RegisterCreatedObjectUndo(go, "Build Account Menu");
        return go.AddComponent<AccountManager>();
    }

    /// <summary>
    /// The "Account" button: bottom right of the side panel, in a row of its own just above
    /// the Leaderboard button and the gear, and as wide as the two of them together.
    /// </summary>
    private static Button BuildOpenButton(Transform sidePanel, out TMP_Text label)
    {
        GameObject go = NewUI("AccountButton", sidePanel);
        // The Leaderboard/gear row is 80 tall, 24 up from the bottom; leave a 16 gap above it.
        Anchor(Rect(go), new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 120), new Vector2(376, 80));
        Image background = AddImage(go, ButtonColor, UISprite());
        background.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = background;

        label = AddText(go, "Label", "Account", 34);
        Stretch(label.rectTransform, 12);
        // It shows the player's name when signed in, which could be long or contain '<'.
        // Ellipsis cuts a long one short with "...", and with rich text off, a name like
        // "<b>Bob" is shown as typed rather than read as formatting.
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.richText = false;

        Undo.RegisterCreatedObjectUndo(go, "Build Account Menu");
        return button;
    }

    private static void BuildWindow(GameObject root, AccountMenu menu)
    {
        GameObject modal = AddModal(root, "Panel", out GameObject window);

        TMP_Text title = AddText(window, "Title", "Account", 56);
        title.fontStyle = FontStyles.Bold;
        PreferredHeight(title.gameObject, 76);

        TMP_Text status = AddText(window, "Status", "Not signed in", 34);
        PreferredHeight(status.gameObject, 50);

        TMP_Text nameText = AddText(window, "Name", "", 44);
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = HighlightColor;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.richText = false;
        PreferredHeight(nameText.gameObject, 60);

        // No fixed height: a TMP text reports the height its (wrapped) text needs, and the
        // window's layout group uses that, so a long message just makes the window taller.
        TMP_Text message = AddText(window, "Message", "", 28);
        message.color = MessageColor;
        message.textWrappingMode = TextWrappingModes.Normal;
        message.richText = false;

        // Sign in, Sign out and Cancel (while waiting for the browser) share a row; only one
        // of them is ever shown.
        GameObject actionRow = AddCenteredRow(window, "ActionRow", 90, 0);
        Button signInButton = AddButton(actionRow.transform, "SignInButton", "Sign in with Google", 460, 80);
        Button signOutButton = AddButton(actionRow.transform, "SignOutButton", "Sign out", 300, 80);
        Button cancelSignInButton = AddButton(actionRow.transform, "CancelSignInButton", "Cancel", 300, 80);

        GameObject closeRow = AddCenteredRow(window, "CloseRow", 90, 0);
        Button closeButton = AddButton(closeRow.transform, "CloseButton", "Close", 300, 80);

        SetReference(menu, "panel", modal);
        SetReference(menu, "statusText", status);
        SetReference(menu, "nameText", nameText);
        SetReference(menu, "messageText", message);
        SetReference(menu, "signInButton", signInButton);
        SetReference(menu, "signOutButton", signOutButton);
        SetReference(menu, "cancelSignInButton", cancelSignInButton);
        SetReference(menu, "closeButton", closeButton);

        // Hidden until opened.
        modal.SetActive(false);
    }
}
