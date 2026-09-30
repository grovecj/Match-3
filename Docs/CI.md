# CI builds

GitHub Actions and [GameCI](https://game.ci) build the game in the cloud (issue #23):

- **Pull requests** build **Windows only**, as a check that the player build still compiles.
  No artifacts are uploaded.
- **Release builds are manual.** **Actions → Build → Run workflow**, pick a branch (usually
  `main`), builds **Windows and Web** and uploads both. Merges to `main` don't build on their
  own: with lots of small PRs, one release per merge isn't needed. The `push` trigger is
  commented out in the workflow if you want it back.
 The workflow is
[`.github/workflows/build.yml`](../.github/workflows/build.yml). Its comments explain each step.

## What it does

| | |
|---|---|
| **Triggers** | `pull_request`, only when `Assets/`, `Packages/`, `ProjectSettings/` or the workflow file changes. `workflow_dispatch` (**Run workflow**) on any branch. `push` to `main` is commented out. |
| **Runner** | `ubuntu-latest`. Unity runs headless inside GameCI's Docker image `unityci/editor:ubuntu-6000.5.7f1-<platform>-3`. |
| **Matrix** | PRs: `StandaloneWindows64` (Mono). Run workflow: `StandaloneWindows64` and `WebGL`, in parallel. All use GameCI's default build method with the Player Settings committed in `ProjectSettings/ProjectSettings.asset`. |
| **Caches** | Git LFS objects (`.git/lfs`), keyed on the list of LFS files. Unity's `Library/`, per platform, keyed on `Assets/`, `Packages/` and `ProjectSettings/`. |
| **Artifacts** | Manual runs only: `Match3-StandaloneWindows64` and `Match3-WebGL`, kept for 7 days. Download them from the bottom of a run's summary page. |
| **Pinned versions** | `game-ci/unity-builder` is pinned to the v6.0.0 commit SHA and its `game-ci/cli` to `v0.1.69`, because that step receives the Unity credentials. Upgrade both on purpose after reading the release notes. |
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
- **Cache scope.** GitHub lets a PR read its own caches and `main`'s, but not another PR's,
  and `main` can't read PR caches. Since merges no longer build, **`main` only gets a cache
  when you Run workflow on `main`**. Without one, each new PR's first build is cold (~33 min
  instead of ~7-11). So run a build on `main` now and then, for example after merging a batch
  of PRs. That also refreshes the cache PRs start from.
- **Eviction.** Caches unused for 7 days are deleted, and the repo gets 10 GB in total (each
  `Library/` cache is ~1.2-1.4 GB). Deleting a merged PR's caches (**Actions → Caches**) frees
  room so `main`'s aren't pushed out.

## Budget (private repo)

The repo is private, so Actions minutes and artifact storage count against the free quota
(2,000 Linux minutes a month on GitHub Free). A PR push costs ~7-11 min cached (~33 cold), so
roughly 200 pushes a month. A manual Windows + Web build costs ~25 min cached (~97 cold). The
path filters and PR cancellation keep it down further.

## Timings

Measured on PR #45 (2026-09-29/30), `ubuntu-latest`. "Unity" is the *Build with Unity* step and
"Job" is the whole job, including pulling the Docker image and freeing disk space.

| Run | Windows (Unity / Job) | Web (Unity / Job) |
|---|---|---|
| Cold (no `Library/` cache) | 31 / 33 min | 60 / 64 min |
| Cached (`Library/` hit) | 5–7 / 7–11 min | 13.5 / 16 min |

- **Web is slow cold because of IL2CPP.** Almost all of it is compiling C# to C++ and then to
  WebAssembly on a 2-core runner. That's why `timeout-minutes` is 90: at 60, a cold Web build
  was killed on step 494 of 495.
- **The cache matters most for Web** (about 4× faster), because the IL2CPP output is kept in
  `Library/Bee`.
- **A failed or cancelled job doesn't save its cache,** so the run after a failure is cold again.
- **The first manual build on `main` is cold** (~64 min for Web), because `main` can't read PR
  caches. Later ones start from its own cache.
- **Cache sizes:** each `Library/` cache is about 1.2–1.4 GB, well within the repo's 10 GB.
- **Artifacts:** Windows is about 49 MB and Web about 17 MB, zipped.

## Web settings live in ProjectSettings.asset

CI doesn't run `WebBuild.cs`. GameCI's build method uses whatever Player Settings are saved in
`ProjectSettings/ProjectSettings.asset`, and **Match3 → Build Web** writes the Web settings
(Gzip, Decompression Fallback, the FullWindow template, ...) there. So if you change the settings
in `WebBuild.ApplyWebSettings`, run **Match3 → Build Web** once and commit
`ProjectSettings.asset`, or CI keeps building with the old ones.

Why not have CI call `WebBuild.BuildFromCommandLine`? GameCI doesn't take Unity's exit code
alone as proof of success. It also looks in the log for the report its own build method prints
(`Build succeeded!` or a `# Build results #` section). A custom build method that doesn't print
that report is marked as failed even when the build worked. That happened on this PR's first run.
