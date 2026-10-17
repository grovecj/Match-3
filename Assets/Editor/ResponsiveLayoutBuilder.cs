using System.Collections.Generic;
using Match3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static UIBuilder;

/// <summary>
/// Editor-only helper that sets up the portrait layout for phones (issue #114): it adds a
/// <see cref="ResponsiveLayout"/> to the Canvas and wires it to the side panel, the things in
/// it, the pause overlay, the achievement toast and every modal window, then points the
/// <see cref="BoardCamera"/> at it. Run it from the menu bar: Match3 > Build Responsive Layout,
/// then save the scene.
///
/// The portrait arrangement itself is in ResponsiveLayout, which moves the objects the scene
/// already has; nothing new is built, and the scene keeps its landscape layout in the Editor.
/// The other menu builders re-create the buttons and windows they own, so they call
/// <see cref="RewireIfPresent"/> to keep these references from going missing.
/// </summary>
public static class ResponsiveLayoutBuilder
{
    // Left to right along the bottom of the portrait panel.
    private static readonly string[] MenuButtons =
    {
        "AchievementsButton", "AccountButton", "LeaderboardButton", "SettingsButton",
    };

    // Paths from the Canvas to each modal window (made by the other menu builders).
    private static readonly string[] Windows =
    {
        "Settings/Modal/Window",
        "Leaderboard/Panel/Window",
        "Leaderboard/Prompt/Window",
        "Achievements/Panel/Window",
        "Account/Panel/Window",
    };

    [MenuItem("Match3/Build Responsive Layout")]
    public static void Build()
    {
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var boardCamera = Object.FindAnyObjectByType<BoardCamera>();
        if (canvas == null || boardCamera == null || canvas.transform.Find("ScorePanel") == null)
        {
            Debug.LogError("Build Responsive Layout: open the Game scene first (needs Canvas/ScorePanel and a BoardCamera).");
            return;
        }

        var layout = canvas.GetComponent<ResponsiveLayout>();
        if (layout == null)
        {
            // Undo.AddComponent rather than AddComponent, so Ctrl+Z can take it off again.
            layout = Undo.AddComponent<ResponsiveLayout>(canvas.gameObject);
        }
        Wire(layout);

        // Tell Unity the scene has unsaved changes (the * in the Hierarchy) so Ctrl+S saves them.
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Selection.activeGameObject = canvas.gameObject;
        Debug.Log("Responsive layout wired up. Save the scene (Ctrl+S) to keep it. " +
                  "Try it in the Game view with a portrait resolution or the Simulator.");
    }

    /// <summary>
    /// Re-wires the scene's ResponsiveLayout, if it has one. The other builders call this after
    /// replacing the buttons and windows it points at.
    /// </summary>
    public static void RewireIfPresent()
    {
        var layout = Object.FindAnyObjectByType<ResponsiveLayout>();
        if (layout != null)
        {
            Wire(layout);
        }
    }

    private static void Wire(ResponsiveLayout layout)
    {
        Transform canvas = layout.transform;
        Transform panel = canvas.Find("ScorePanel");

        SetReference(layout, "canvasScaler", canvas.GetComponent<CanvasScaler>());
        SetReference(layout, "sidePanel", panel);
        SetReference(layout, "scoreLabel", Find(panel, "ScoreLabel"));
        SetReference(layout, "scoreValue", Find(panel, "ScoreValue"));
        SetReference(layout, "timeLabel", Find(panel, "TimeLabel"));
        SetReference(layout, "timerValue", Find(panel, "TimerValue"));
        SetReference(layout, "roundOverMessage", Find(panel, "RoundOverMessage"));
        SetReference(layout, "roundButton", Find(panel, "RoundButton"));
        SetArray(layout, "menuButtons", FindAll(panel, MenuButtons));

        SetReference(layout, "pauseOverlay", Find(canvas, "PauseOverlay"));
        SetReference(layout, "achievementToast", Find(canvas, "Achievements/Toast"));
        SetArray(layout, "windows", FindAll(canvas, Windows));

        var boardCamera = Object.FindAnyObjectByType<BoardCamera>();
        if (boardCamera != null)
        {
            SetReference(boardCamera, "layout", layout);
        }
    }

    /// <summary>The RectTransform at <paramref name="path"/> under <paramref name="root"/>, or null (with a warning).</summary>
    private static RectTransform Find(Transform root, string path)
    {
        var found = root.Find(path) as RectTransform;
        if (found == null)
        {
            Debug.LogWarning($"Build Responsive Layout: no {root.name}/{path}, so it won't be moved for portrait. " +
                             "Run the menu builder that makes it, then this one again.");
        }
        return found;
    }

    private static Object[] FindAll(Transform root, string[] paths)
    {
        var found = new List<Object>();
        foreach (string path in paths)
        {
            RectTransform rt = Find(root, path);
            if (rt != null)
            {
                found.Add(rt);
            }
        }
        return found.ToArray();
    }
}
