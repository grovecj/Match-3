# CLAUDE.md

Match-3 game (Bejeweled-style) in Unity. Learning project: favor clear, simple,
well-explained code over polish. Explain Unity concepts when introducing them.

- Unity 6000.5.7f1, 2D URP template, new Input System (`com.unity.inputsystem`). Target: PC.
- Repo root is the Unity project root. Source art goes in `Art/`, game assets in `Assets/`.
- Tasks are tracked in GitHub Issues (`gh issue list -R grovecj/Match-3`).
- Unity must be closed before moving/renaming files outside the Editor; always move `.meta` files with their assets.
- Editor-only tools (e.g. `Match3 > Build Settings Menu`, which regenerates the Settings UI and
  its option prefabs, and `Match3 > Build Leaderboard Menu`) live in `Assets/Editor/`, sharing
  helpers from `UIBuilder.cs`. `Match3 > Build Web` makes the browser build into `Builds/Web`
  (see `Docs/WebBuild.md`).
- CI (`.github/workflows/build.yml`, GameCI) builds Windows on PRs that touch `Assets/`,
  `Packages/`, `ProjectSettings/` or the workflow; Windows + Web builds are manual
  (Actions → Build → Run workflow). A Run workflow on `main` with **Deploy** ticked also
  releases: Web via the `web-build` branch (App Platform), Windows zip to Spaces. See
  `Docs/CI.md`. Needs the `UNITY_LICENSE`/`UNITY_EMAIL`/`UNITY_PASSWORD` repo secrets, plus
  `SPACES_ACCESS_KEY_ID`/`SPACES_SECRET_ACCESS_KEY`/`DIGITALOCEAN_ACCESS_TOKEN` to deploy.
  DigitalOcean resources are Terraform in grovecj/cartergrove-me-infra, not here.
  `.github/workflows/test.yml` runs the Edit Mode + Play Mode tests on the same PRs (and via
  Actions → Test → Run workflow); failures show in the PR's **Test Results** check.
- Game scripts live in `Assets/Scripts/` (the `Match3` assembly). Edit Mode tests live in
  `Assets/Tests/EditMode/` (`Match3.Tests.EditMode`); Play Mode tests live in
  `Assets/Tests/PlayMode/` (`Match3.Tests.PlayMode`, which can see `Match3`'s `internal` test
  hooks via `Assets/Scripts/AssemblyInfo.cs`). Run them from **Window → General → Test
  Runner → EditMode / PlayMode**, or headless (Unity must not already have the project open):
  `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`
  (or `-testPlatform PlayMode`)
- Don't commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, or generated `.csproj`/`.sln*` files.
