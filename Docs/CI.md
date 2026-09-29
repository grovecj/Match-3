# CI builds

Every pull request and every merge to `main` builds the game for **Windows** and the **Web**
with GitHub Actions and [GameCI](https://game.ci) (issue #23). The workflow is
[`.github/workflows/build.yml`](../.github/workflows/build.yml). Its comments explain each step.

## What it does

| | |
|---|---|
| **Triggers** | `pull_request` and `push` to `main`, only when `Assets/`, `Packages/`, `ProjectSettings/` or the workflow file changes. Also **Actions → Build → Run workflow** by hand. |
| **Runner** | `ubuntu-latest`. Unity runs headless inside GameCI's Docker image `unityci/editor:ubuntu-6000.5.7f1-<platform>-3`. |
| **Matrix** | `StandaloneWindows64` (Mono, GameCI's default build method) and `WebGL` (our `WebBuild.BuildFromCommandLine`, the same code as **Match3 → Build Web**). The two jobs run in parallel. |
| **Caches** | Git LFS objects (`.git/lfs`), keyed on the list of LFS files. Unity's `Library/`, per platform, keyed on `Assets/`, `Packages/` and `ProjectSettings/`. |
| **Artifacts** | `Match3-StandaloneWindows64` and `Match3-WebGL`, kept for 7 days. Download them from the bottom of a run's summary page. |
| **Fails when** | Unity exits non-zero: a C# compile error, a failed build, or a license problem. |

Unity version check: GameCI publishes images for **6000.5.7f1** (`ubuntu-6000.5.7f1-windows-mono-3`
and `ubuntu-6000.5.7f1-webgl-3`, checked on Docker Hub 2026-09-29), so no workaround is needed.
When the project upgrades Unity, check that
[images for the new version exist](https://hub.docker.com/r/unityci/editor/tags) first.

## One-time setup: Unity license secrets

Unity won't run without a license, even in CI. A free Personal license works. The repo owner
does this once. **Never commit the license file or credentials.** They live only in GitHub's
encrypted secrets.

1. In **Unity Hub → Preferences → Licenses**, click **Add** → **Get a free personal license**.
   Click Add even if a license is already listed, because that's what writes the license file.
2. Open `C:\ProgramData\Unity\Unity_lic.ulf` in a text editor and copy all of it.
3. On GitHub: **Settings → Secrets and variables → Actions → New repository secret**, and add:
   - `UNITY_LICENSE`: the full contents of `Unity_lic.ulf`
   - `UNITY_EMAIL`: your Unity account email
   - `UNITY_PASSWORD`: your Unity account password
4. Re-run the workflow (**Actions → Build → Run workflow**).

The workflow reads these as `${{ secrets.NAME }}`. GitHub masks them as `***` in logs.
Pull requests from **forks** don't get secrets, so their builds fail at activation. That's
expected: only branches in this repo can be built.

## How the caches work

- **Library cache.** `Library/` is Unity's import cache: every texture, script and shader converted
  for the target platform. A cold build spends most of its time filling it. With the cache,
  Unity only re-imports what changed. The key includes a hash of `Assets/`, `Packages/` and
  `ProjectSettings/`, so any change misses the exact key. `restore-keys` then falls back to the
  newest `Library-<platform>-` cache, which is almost as good.
- **LFS cache.** The PNGs and fonts are in Git LFS. The free plan allows 1 GB of LFS
  downloads a month, and each fresh runner would download all of them. The cache means they
  are downloaded again only when the set of LFS files changes.
- **Cache scope.** GitHub lets a PR read caches made on `main` but not the other way round.
  So the first build of a new PR starts from `main`'s cache, and merging to `main` refreshes it.
  Caches unused for 7 days are evicted, and the repo gets 10 GB in total.

## Budget (private repo)

The repo is private, so Actions minutes and artifact storage count against the free quota
(2,000 Linux minutes a month on GitHub Free). Each run uses two jobs' worth of minutes. The path
filters and PR cancellation keep that down. If minutes get tight, drop the `push` trigger or
build Windows only on PRs.

## Timings

Fill in from the run summaries (Actions → a run → each job's duration).

| Run | Windows | Web |
|---|---|---|
| Cold (no caches) | _TBD_ | _TBD_ |
| Cached (Library + LFS hit) | _TBD_ | _TBD_ |

## Running the same build locally

The Web job runs the same method as the Editor menu:

```bash
Unity.exe -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod WebBuild.BuildFromCommandLine -customBuildPath build/WebGL/Match3
```

Leave out `-customBuildPath` to build into `Builds/Web`, like the menu does.
