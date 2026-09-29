using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Editor-only tool that makes a Web (WebGL) build of the game (issue #22) into Builds/Web.
/// Run it from the menu bar: Match3 > Build Web, or Match3 > Build Web and Run to also open it
/// in your browser. See Docs/WebBuild.md for why each setting is what it is.
///
/// You could set all of this by hand in File > Build Profiles and Project Settings > Player >
/// Web. Doing it in code means the settings are written down, reviewable in a PR, and the same
/// every time, and a CI server can make the exact same build later:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod WebBuild.BuildFromCommandLine
///
/// A Web build always compiles C# with IL2CPP: C# -> C++ -> WebAssembly (via Emscripten).
/// Browsers can't run the Mono runtime the Editor uses, so there's no Mono option for Web, and
/// that C++ compile is why a Web build takes minutes instead of seconds.
/// </summary>
public static class WebBuild
{
    private const string OutputFolder = "Builds/Web";

    [MenuItem("Match3/Build Web")]
    public static void Build()
    {
        RunBuild(BuildOptions.None);
    }

    // AutoRunPlayer makes Unity start a small local web server for the build and open it in
    // your default browser, like File > Build And Run.
    [MenuItem("Match3/Build Web and Run")]
    public static void BuildAndRun()
    {
        RunBuild(BuildOptions.AutoRunPlayer);
    }

    /// <summary>For -executeMethod: builds, then quits Unity with exit code 0 on success, 1 on failure.</summary>
    public static void BuildFromCommandLine()
    {
        bool succeeded = RunBuild(BuildOptions.None);
        EditorApplication.Exit(succeeded ? 0 : 1);
    }

    /// <summary>Applies the Web player settings and builds every scene in the build list.</summary>
    private static bool RunBuild(BuildOptions options)
    {
        ApplyWebSettings();

        var buildOptions = new BuildPlayerOptions
        {
            // The scenes ticked in the build profile's Scene List, in order.
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = OutputFolder,
            target = BuildTarget.WebGL,
            options = options,
        };

        // BuildPlayer switches the Editor to the Web platform first if it isn't already on it.
        // The first switch re-imports textures for the new platform, which takes a while.
        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Web build {summary.result} with {summary.totalErrors} error(s). See the Console above.");
            return false;
        }

        // summary.totalSize counts everything Unity wrote, including the uncompressed index.html
        // and template files. What a player actually downloads is the Build folder, so we
        // measure that too; those are the numbers worth writing down in Docs/WebBuild.md.
        long downloadBytes = new DirectoryInfo(Path.Combine(OutputFolder, "Build"))
            .GetFiles().Sum(f => f.Length);
        Debug.Log($"Web build succeeded in {summary.totalTime:m\\:ss}. " +
                  $"Download size (Build folder): {downloadBytes / 1024f / 1024f:0.0} MB. Output: {Path.GetFullPath(OutputFolder)}");
        return true;
    }

    private static void ApplyWebSettings()
    {
        // Gzip rather than Brotli: Brotli files are ~10-20% smaller, but browsers only accept
        // Brotli over HTTPS, it's slower to compress at build time, and its fallback
        // decompressor (below) is slower. Gzip works everywhere, including plain http://.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;

        // The build files are pre-compressed (Game.wasm.gz etc). A web server is supposed to
        // send them with a "Content-Encoding: gzip" header so the browser unzips them itself.
        // Many simple hosts (GitHub Pages, python -m http.server, itch.io) don't, and without
        // this setting the game then fails to load. Decompression Fallback makes the loader
        // unzip them in JavaScript when the header is missing: a little slower, always works.
        PlayerSettings.WebGL.decompressionFallback = true;

        // Our own template in Assets/WebGLTemplates/FullWindow. "PROJECT:" means "from this
        // project" ("APPLICATION:" would be one of Unity's built-in ones).
        PlayerSettings.WebGL.template = "PROJECT:FullWindow";

        // Keep the downloaded build in the browser's cache (IndexedDB) so reloads are faster.
        PlayerSettings.WebGL.dataCaching = true;

        // Keep Unity's main loop running when the page loses focus. With this off, a Web build
        // stops rendering the moment you click outside it, so resizing the window while it's
        // unfocused leaves a blank canvas until you click back in. We don't need Unity to
        // freeze the game for us: GameManager already pauses the round on focus loss, and
        // browsers throttle hidden tabs on their own.
        PlayerSettings.runInBackground = true;

        // Player Settings live in ProjectSettings/ProjectSettings.asset; make sure they're saved.
        AssetDatabase.SaveAssets();
    }
}
