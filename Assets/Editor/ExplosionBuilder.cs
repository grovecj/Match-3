using Match3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static UIBuilder;

/// <summary>
/// Editor-only helper that builds the Explosion prefab (issue #54): the particle burst the
/// Board spawns on every cleared cell. Run it from the menu bar: Match3 > Build Explosion
/// Prefab. It also assigns the prefab to the Board if the Game scene is open (then save the
/// scene).
///
/// Building it from code rather than by hand in the Inspector means every setting is written
/// down, explained, and easy to rebuild. Running it again overwrites what it built before.
/// Afterwards you can still open the prefab and tweak it by hand, but the next run of this
/// tool will undo those tweaks, so change the numbers here to keep them.
/// </summary>
public static class ExplosionBuilder
{
    private const string SparkTexturePath = "Assets/Sprites/Effects/Spark.png";
    private const string GlowTexturePath = "Assets/Sprites/Effects/Glow.png";
    private const string MaterialFolder = "Assets/Materials";
    private const string SparkMaterialPath = MaterialFolder + "/ExplosionSpark.mat";
    private const string GlowMaterialPath = MaterialFolder + "/ExplosionGlow.mat";
    private const string PrefabPath = "Assets/Prefabs/Explosion.prefab";

    // URP's unlit sprite shader: it draws in the 2D Renderer, multiplies the texture by each
    // particle's color, and ignores 2D lights (an explosion should glow, not be shaded).
    private const string ShaderName = "Universal Render Pipeline/2D/Sprite-Unlit-Default";

    // Sorting order decides what's drawn on top within a sorting layer: cells are 0 and tokens
    // are 1, so the flash goes just above the tokens and the shards above that.
    private const int FlashSortingOrder = 2;
    private const int ShardsSortingOrder = 3;

    [MenuItem("Match3/Build Explosion Prefab")]
    public static void Build()
    {
        Material sparkMaterial = BuildMaterial(SparkTexturePath, SparkMaterialPath);
        Material glowMaterial = BuildMaterial(GlowTexturePath, GlowMaterialPath);
        if (sparkMaterial == null || glowMaterial == null)
        {
            return;
        }

        // Build the object in the open scene, save a prefab from it, then delete the original.
        var root = new GameObject("Explosion");
        BuildShards(root, sparkMaterial);
        var flashObject = new GameObject("Flash");
        flashObject.transform.SetParent(root.transform, false);
        ParticleSystem flash = BuildFlash(flashObject, glowMaterial);

        var explosion = root.AddComponent<Explosion>();
        SetReference(explosion, "flash", flash);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        Debug.Log($"Explosion prefab built at {PrefabPath}.", prefab);

        // Hook it up to the Board, if the Game scene is open.
        var board = Object.FindAnyObjectByType<Board>();
        if (board == null)
        {
            Debug.LogWarning("Build Explosion Prefab: no Board in the open scene, so assign the prefab " +
                             "to the Board's Explosion Prefab field yourself.");
            return;
        }
        SetReference(board, "explosionPrefab", prefab.GetComponent<Explosion>());
        EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
        Debug.Log("Assigned it to the Board. Save the scene (Ctrl+S) to keep it.", board);
    }

    /// <summary>
    /// Sets up the texture's import settings for particles and creates (or updates) a material
    /// that draws it. Returns null, after logging why, if something's missing.
    /// </summary>
    private static Material BuildMaterial(string texturePath, string materialPath)
    {
        // A TextureImporter holds the settings shown when you select an image in the Project
        // window. Particles want a plain texture (not a Sprite), clamped so the edges don't
        // wrap around, with no mipmaps (they're tiny on screen anyway).
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"Build Explosion Prefab: no texture at {texturePath}. " +
                           "Run Art/Effects/build_particles.py first.");
            return null;
        }
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"Build Explosion Prefab: can't find the shader '{ShaderName}'. Is URP installed?");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        // Update the existing material if there is one, so anything referencing it (the old
        // prefab) keeps working and its GUID in version control stays the same.
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.shader = shader;
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        return material;
    }

    /// <summary>The colored shards: a quick burst of sparkles flying out in a circle.</summary>
    private static void BuildShards(GameObject go, Material material)
    {
        // Adding a ParticleSystem also adds the ParticleSystemRenderer that draws it.
        var particles = go.AddComponent<ParticleSystem>();
        // Some settings (like duration) can't change while a system is playing, so make sure
        // it isn't.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Main: settings for the system as a whole, and each particle's starting values.
        // "MinMaxCurve(a, b)" means "a random value between a and b for each particle".
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.1f;          // How long it emits for. The burst fires at 0, so this barely matters.
        main.loop = false;             // Fire once, not over and over.
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
        // Explosion.Play overwrites the speed (and color and count) for each match size.
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI); // In radians: any angle.
        main.gravityModifier = 0f;
        main.maxParticles = 64;
        // Local: particles move with the Explosion's transform, which also means scaling that
        // transform (the Board scales it by its cell size) scales the whole effect.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        // Scaled time (the default): Time.timeScale = 0 freezes particles mid-flight on pause.
        main.useUnscaledTime = false;
        main.playOnAwake = false;      // Explosion.Play starts it, once it's been colored.
        // When the system has finished and its last particle has died, destroy the GameObject.
        // That's all the cleanup a one-shot effect needs.
        main.stopAction = ParticleSystemStopAction.Destroy;

        // Emission: no steady stream, just one burst (Explosion.Play sets its size).
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });

        // Shape: where particles start and which way they head. A circle in the XY plane (the
        // 2D screen plane) sends them outward in every direction, from a small ring.
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.15f;
        shape.arc = 360f;
        shape.rotation = Vector3.zero;

        // Limit Velocity: drag slows the shards down as they go, so they burst out fast and
        // then drift, rather than flying off at a constant speed.
        ParticleSystem.LimitVelocityOverLifetimeModule limitVelocity = particles.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.drag = 4f;

        // Color over Lifetime: multiplied onto the start color. Keep full color, but fade the
        // alpha out over the second half of each particle's life.
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(FadeOut(0.5f));

        // Size over Lifetime: multiplied onto the start size. Shrink steadily from full size to 0.
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        SetUpRenderer(go, material, ShardsSortingOrder);
    }

    /// <summary>
    /// The white flash behind a 5+ burst: one big, soft glow that swells slightly and fades fast.
    /// </summary>
    private static ParticleSystem BuildFlash(GameObject go, Material material)
    {
        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.05f;
        main.loop = false;
        // Shorter than any shard, so it's always gone before the shards finish and destroy
        // the whole Explosion (this flash included).
        main.startLifetime = 0.18f;
        main.startSpeed = 0f;          // Stays put in the middle of the cell.
        main.startSize = 1.8f;         // Bigger than a cell, so it lights up the neighbors a little.
        main.startColor = new Color(1f, 1f, 1f, 0.8f);
        main.maxParticles = 1;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.useUnscaledTime = false;
        main.playOnAwake = false;      // Explosion.Play only plays it for 5+.

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

        // No shape: the single particle starts exactly at the center.
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(FadeOut(0f));

        // Grow from 60% to full size as it fades, like a shockwave.
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1f));

        SetUpRenderer(go, material, FlashSortingOrder);
        return particles;
    }

    /// <summary>
    /// A white gradient whose alpha holds at 1 until <paramref name="fadeStart"/> (0 to 1 of the
    /// particle's life), then fades to 0 at the end.
    /// </summary>
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
        // Billboard: each particle is a flat square facing the camera, textured with the material.
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortingOrder;
    }
}
