using Match3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static UIBuilder;

/// <summary>
/// Editor-only helper for issue #86 (power gem effects, sounds and score popups). Run it from
/// the menu bar: Match3 > Build Power Gem Effects, with the Game scene open, then save the scene.
/// Run Match3 > Build Explosion Prefab first: these effects share its textures and materials.
///
/// It does two things:
/// <list type="number">
/// <item>Builds four particle prefabs in Assets/Prefabs/Effects/ (a new gem's flash, a Flame's
/// blast, a Star's burst, a Hypercube's spark), each with a <see cref="ParticleEffect"/> on it.</item>
/// <item>Adds the listeners to the open scene and wires them up: <see cref="PowerGemEffects"/>
/// and <see cref="ScorePopups"/> on a "Power Gem Effects" object, <see cref="PowerGemSounds"/>
/// on the AudioManager, and <see cref="ScreenShake"/> on the camera.</item>
/// </list>
///
/// Like the Explosion builder, every particle setting is written down here, so running it again
/// rebuilds the prefabs exactly. Tweak the numbers here rather than in the prefabs.
/// </summary>
public static class PowerGemEffectsBuilder
{
    private const string SparkMaterialPath = "Assets/Materials/ExplosionSpark.mat";
    private const string GlowMaterialPath = "Assets/Materials/ExplosionGlow.mat";
    private const string PrefabFolder = "Assets/Prefabs/Effects";

    // Where each prefab is saved. PowerGemEffectPrefabAssetTests checks these same paths.
    private const string GemCreatedPath = PrefabFolder + "/GemCreated.prefab";
    private const string FlameBlastPath = PrefabFolder + "/FlameBlast.prefab";
    private const string StarBurstPath = PrefabFolder + "/StarBurst.prefab";
    private const string ZapSparkPath = PrefabFolder + "/ZapSpark.prefab";

    // Cells are 0 and tokens 1; beams and bolts sit at the tokens' order + 10 (11). The
    // particles go in front of all of those, glow behind sparks.
    private const int GlowSortingOrder = 12;
    private const int SparkSortingOrder = 13;

    private const string EffectsObjectName = "Power Gem Effects";

    /// <summary>The numbers that make one effect different from another.</summary>
    // A struct is a small bundle of values. Each effect below is one of these filled in.
    private struct Recipe
    {
        public string Name;
        public int Count;              // How many sparks.
        public Vector2 Speed;          // Slowest and fastest, in cells per second.
        public Vector2 Lifetime;       // Shortest and longest, in seconds.
        public Vector2 Size;           // Smallest and biggest, in cells.
        public float Radius;           // How far from the middle sparks start, in cells.
        public float Drag;             // How fast sparks slow down. Higher stops them sooner.
        public float GlowSize;         // How wide the glow is, in cells. 0 for no glow.
        public float GlowLifetime;     // In seconds. Must end before the shortest spark does.
        public float GlowAlpha;        // How see-through the glow is (1 = solid).
    }

    [MenuItem("Match3/Build Power Gem Effects")]
    public static void Build()
    {
        var sparkMaterial = AssetDatabase.LoadAssetAtPath<Material>(SparkMaterialPath);
        var glowMaterial = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
        if (sparkMaterial == null || glowMaterial == null)
        {
            Debug.LogError("Build Power Gem Effects: the Explosion's materials are missing. Run Match3 > Build Explosion Prefab first.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Effects");
        }

        // A new gem: a quick ring of sparkles and a glow that swells, as it springs into place.
        ParticleEffect gemCreated = BuildPrefab(GemCreatedPath, sparkMaterial, glowMaterial, new Recipe
        {
            Name = "GemCreated", Count = 18, Speed = new Vector2(2f, 3.5f), Lifetime = new Vector2(0.25f, 0.35f),
            Size = new Vector2(0.15f, 0.3f), Radius = 0.2f, Drag = 5f,
            GlowSize = 1.6f, GlowLifetime = 0.22f, GlowAlpha = 0.7f,
        });
        // A Flame: lots of big, fast embers that fly far enough to cover the 3x3 (about 1.5
        // cells out), over a glow three cells wide.
        ParticleEffect flameBlast = BuildPrefab(FlameBlastPath, sparkMaterial, glowMaterial, new Recipe
        {
            Name = "FlameBlast", Count = 48, Speed = new Vector2(6f, 10f), Lifetime = new Vector2(0.3f, 0.4f),
            Size = new Vector2(0.25f, 0.45f), Radius = 0.3f, Drag = 6f,
            GlowSize = 3.4f, GlowLifetime = 0.25f, GlowAlpha = 0.85f,
        });
        // A Star: a tight, bright burst on the gem; the beams (built in code) do the travelling.
        ParticleEffect starBurst = BuildPrefab(StarBurstPath, sparkMaterial, glowMaterial, new Recipe
        {
            Name = "StarBurst", Count = 20, Speed = new Vector2(3f, 5f), Lifetime = new Vector2(0.2f, 0.3f),
            Size = new Vector2(0.15f, 0.3f), Radius = 0.1f, Drag = 5f,
            GlowSize = 1.4f, GlowLifetime = 0.18f, GlowAlpha = 0.8f,
        });
        // A Hypercube's spark: small and quick, since a dozen can go off one after another.
        ParticleEffect zapSpark = BuildPrefab(ZapSparkPath, sparkMaterial, glowMaterial, new Recipe
        {
            Name = "ZapSpark", Count = 8, Speed = new Vector2(2f, 4f), Lifetime = new Vector2(0.15f, 0.25f),
            Size = new Vector2(0.12f, 0.22f), Radius = 0.05f, Drag = 6f,
            GlowSize = 0.9f, GlowLifetime = 0.12f, GlowAlpha = 0.8f,
        });
        AssetDatabase.SaveAssets();
        Debug.Log($"Power gem effect prefabs built in {PrefabFolder}.");

        WireScene(gemCreated, flameBlast, starBurst, zapSpark);
    }

    /// <summary>Adds (or updates) the effect, sound, shake and popup listeners in the open scene.</summary>
    private static void WireScene(ParticleEffect gemCreated, ParticleEffect flameBlast, ParticleEffect starBurst, ParticleEffect zapSpark)
    {
        var board = Object.FindAnyObjectByType<Board>();
        var audioManager = Object.FindAnyObjectByType<AudioManager>();
        var boardCamera = Object.FindAnyObjectByType<BoardCamera>();
        if (board == null || audioManager == null || boardCamera == null)
        {
            Debug.LogWarning("Build Power Gem Effects: open the Game scene (it needs a Board, an AudioManager and " +
                             "a BoardCamera) and run this again to add the effects, sounds, shake and popups to it.");
            return;
        }

        // The effects and popups get an object of their own, at the scene's root with no
        // scale, so the copies they make are sized only by the board's cell size.
        GameObject effectsObject = GameObject.Find(EffectsObjectName);
        if (effectsObject == null)
        {
            effectsObject = new GameObject(EffectsObjectName);
            Undo.RegisterCreatedObjectUndo(effectsObject, "Build Power Gem Effects");
        }
        PowerGemEffects effects = GetOrAdd<PowerGemEffects>(effectsObject);
        SetReference(effects, "board", board);
        SetReference(effects, "gemCreatedPrefab", gemCreated);
        SetReference(effects, "flameBlastPrefab", flameBlast);
        SetReference(effects, "starBurstPrefab", starBurst);
        SetReference(effects, "zapSparkPrefab", zapSpark);
        SetReference(GetOrAdd<ScorePopups>(effectsObject), "board", board);

        PowerGemSounds sounds = GetOrAdd<PowerGemSounds>(audioManager.gameObject);
        SetReference(sounds, "board", board);
        SetReference(sounds, "audioManager", audioManager);

        SetReference(GetOrAdd<ScreenShake>(boardCamera.gameObject), "board", board);

        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        Selection.activeGameObject = effectsObject;
        Debug.Log("Power gem effects, sounds, screen shake and score popups added. Save the scene (Ctrl+S) to keep them.");
    }

    /// <summary>The <typeparamref name="T"/> on <paramref name="go"/>, added if it isn't there yet.</summary>
    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(go);
    }

    /// <summary>
    /// Builds one effect: sparks flying out from the middle (the root ParticleSystem, which
    /// reports when it's done), with a soft glow behind them (a child), and saves it as a prefab.
    /// </summary>
    private static ParticleEffect BuildPrefab(string path, Material sparkMaterial, Material glowMaterial, Recipe recipe)
    {
        var root = new GameObject(recipe.Name);
        BuildSparks(root, sparkMaterial, recipe);

        ParticleSystem glow = null;
        if (recipe.GlowSize > 0f)
        {
            var glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(root.transform, false);
            glow = BuildGlow(glowObject, glowMaterial, recipe);
        }

        var effect = root.AddComponent<ParticleEffect>();
        SetReference(effect, "glow", glow);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<ParticleEffect>();
    }

    /// <summary>The coloured sparks. See ExplosionBuilder.BuildShards for what each module does.</summary>
    private static void BuildSparks(GameObject go, Material material, Recipe recipe)
    {
        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(recipe.Lifetime.x, recipe.Lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(recipe.Speed.x, recipe.Speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(recipe.Size.x, recipe.Size.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.gravityModifier = 0f;
        main.maxParticles = recipe.Count;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.useUnscaledTime = false;
        main.playOnAwake = false;
        // Not Destroy, like the Explosion: Callback calls ParticleEffect.OnParticleSystemStopped,
        // which hands the effect back to its pool to be played again.
        main.stopAction = ParticleSystemStopAction.Callback;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, recipe.Count) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = recipe.Radius;
        shape.arc = 360f;
        shape.rotation = Vector3.zero;

        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity = particles.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.drag = recipe.Drag;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(FadeOut(0.4f));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        SetUpRenderer(go, material, SparkSortingOrder);
    }

    /// <summary>One soft glow in the middle that swells as it fades, like a shockwave.</summary>
    private static ParticleSystem BuildGlow(GameObject go, Material material, Recipe recipe)
    {
        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.05f;
        main.loop = false;
        // Shorter than any spark, so it has always faded before the sparks finish and the
        // effect goes back to its pool.
        main.startLifetime = recipe.GlowLifetime;
        main.startSpeed = 0f;
        main.startSize = recipe.GlowSize;
        main.startColor = new Color(1f, 1f, 1f, recipe.GlowAlpha);
        main.maxParticles = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.useUnscaledTime = false;
        main.playOnAwake = false;
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(FadeOut(0f));

        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1f));

        SetUpRenderer(go, material, GlowSortingOrder);
        return particles;
    }

    /// <summary>White, fully opaque until <paramref name="fadeStart"/> (0 to 1 of a particle's life), then fading out.</summary>
    private static Gradient FadeOut(float fadeStart)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, fadeStart), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    private static void SetUpRenderer(GameObject go, Material material, int sortingOrder)
    {
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortingOrder;
    }
}
