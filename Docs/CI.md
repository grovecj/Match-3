# CI builds

GitHub Actions and [GameCI](https://game.ci) build the game in the cloud (issue #23):

- **Builds are manual.** Go to **Actions → Build → Run workflow** and pick a branch
  (usually `main`, or a PR's branch to check it still builds). That builds **Windows and Web**
  and uploads both. Nothing builds on its own: PRs are usually merged before a build would
  finish, and with lots of small PRs one release per merge isn't needed. The `pull_request`
  and `push` triggers are commented out in the workflow if you want them back.

- **Releases (deploys) are manual too** (issue #24). A **Run workflow** on `main` with
  **Deploy** ticked (the default) also publishes the build: Web to
  <https://games.cartergrove.me/match3/>, and the Windows zip to Spaces. See
  [Deploying](#deploying) below and the README's *Releasing* section.

- **Tests are manual too** (issue #33), in a separate workflow: **Actions → Test → Run
  workflow**. See [Tests](#tests) below.

The workflow is [`.github/workflows/build.yml`](../.github/workflows/build.yml). Its comments explain each step.

## What it does

| | |
|---|---|
| **Triggers** | `workflow_dispatch` (**Run workflow**) on any branch, and nothing else. `pull_request` and `push` to `main` are commented out. |
| **Runner** | `ubuntu-latest`. Unity runs headless inside GameCI's Docker image `unityci/editor:ubuntu-6000.5.7f1-<platform>-3`. |
| **Matrix** | `StandaloneWindows64` (Mono) and `WebGL`, in parallel. (If the `pull_request` trigger is turned back on, PRs build Windows only.) All use GameCI's default build method with the Player Settings committed in `ProjectSettings/ProjectSettings.asset`. |
| **Caches** | Git LFS objects (`.git/lfs`), keyed on the list of LFS files. Unity's `Library/`, per platform, keyed on `Assets/`, `Packages/` and `ProjectSettings/`. |
| **Artifacts** | Manual runs only: `Match3-StandaloneWindows64` and `Match3-WebGL`, kept for 7 days. Download them from the bottom of a run's summary page. |
| **Pinned versions** | `game-ci/unity-builder` is pinned to the v6.0.0 commit SHA and its `game-ci/cli` to `v0.1.69`, because that step receives the Unity credentials. Upgrade both on purpose after reading the release notes. |
| **Deploy** | Only a Run workflow on `main` with **Deploy** ticked, and only after every build succeeded. Pushes the Web build to the `web-build` branch and uploads the Windows zip to Spaces. |
| **Fails when** | Unity exits non-zero: a C# compile error, a failed build, or a license problem. |

Unity version check: GameCI publishes images for **6000.5.7f1** (`ubuntu-6000.5.7f1-windows-mono-3`
and `ubuntu-6000.5.7f1-webgl-3`, checked on Docker Hub 2026-09-29), so no workaround is needed.
When the project upgrades Unity, check that
[images for the new version exist](https://hub.docker.com/r/unityci/editor/tags) first.

## Tests

[`.github/workflows/test.yml`](../.github/workflows/test.yml) runs the Edit Mode and Play Mode
tests with GameCI's [`unity-test-runner`](https://game.ci/docs/github/test-runner). It is the
same as running `Unity.exe -batchmode -runTests` locally, but in the same kind of Docker image
the build uses.

| | |
|---|---|
| **Triggers** | The same as the build: only **Actions → Test → Run workflow**, on any branch. `pull_request` is commented out. |
| **What runs** | `testMode: all`: every test in `Match3.Tests.EditMode`, then every test in `Match3.Tests.PlayMode`. |
| **Fails when** | Any test fails, the code doesn't compile, or the license can't be activated. A PR whose branch was tested then shows a red **Test** check. |
| **Results** | A **Test Results** check run on the commit (from `githubToken`), listing every test with failures annotated. Plus a `Test-results` artifact holding the NUnit XML files, kept 7 days, uploaded even when tests fail. |
| **Caches** | The same LFS cache as the build. Its own `Library-Tests-` cache, because the Editor imports for its default (Linux) target, not Windows or Web. |
| **Pinned versions** | `game-ci/unity-test-runner` is pinned to the v4.4.0 commit SHA and `game-ci/cli` to `v0.1.69`, like the builder, since this step also gets the Unity credentials. |

**Reading a failure.** Open the PR's **Checks** tab and pick **Test Results**. It lists each
failed test with its assertion message and stack trace, and puts an annotation on the failing
line. For the full Unity log, open the **Test** job's *Run tests* step instead.

**Why a separate workflow?** It keeps "tests pass" and "the player builds" as separate checks,
so you can run just the one you want (the tests are much the quicker), and tell at a glance
which one broke when you run both.

**Why `checks: write`?** The `Test Results` check run is created with the workflow's own token,
and creating check runs needs that permission. Otherwise the token can only read the repo.

**Code coverage is off** (`GAME_CI_COVERAGE_ENABLED: false`): it needs the
`com.unity.testtools.codecoverage` package, which this project doesn't have.

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
Only branches in this repo can be built: **Run workflow** doesn't offer a fork's branches, and
a fork wouldn't get the secrets anyway.

## Deploying

The `deploy` job runs after the builds, only on a **Run workflow** on `main` with **Deploy**
ticked. Every DigitalOcean resource it touches is created by Terraform in
[grovecj/cartergrove-me-infra](https://github.com/grovecj/cartergrove-me-infra)
(`projects/games/`). This repo only hands new files over. The bucket name and URLs at the top
of the job come from that project's `terraform output`, so update them if the infra changes.

- **Web → App Platform.** The games hub app has one *static site* component per game. The
  `match3` component serves the root of this repo's **`web-build` branch** at `/match3/`, and
  redeploys whenever that branch changes. So the job turns the Web build folder into a
  one-commit repo and force-pushes it as `web-build`. That's a *deploy branch*: build output
  only, no history, nothing in common with `main`. Don't edit it by hand; the next release
  overwrites it.
- **Windows → Spaces.** Spaces is DigitalOcean's S3-compatible storage, so the AWS CLI uploads
  to it with `--endpoint-url`. The zip goes to the `games-downloads` bucket as
  `match3/Match3-Windows-<short-sha>.zip`, then is copied to `match3/Match3-Windows-latest.zip`.
  Both are `public-read`; the bucket itself isn't listable. Players download through the CDN
  (`games-downloads.nyc3.cdn.digitaloceanspaces.com`), which caches files for an hour, so the
  job purges the CDN's copy of the "latest" zip via the DigitalOcean API.
- **Order.** The Web push happens first. If the Spaces upload then fails, the new Web build is
  live but the zip isn't; fix the problem and run the workflow again.
- **Timing.** App Platform usually has the new build live a few minutes after the push.
  Watch it under the `games-hub` app's **Activity** tab in the DigitalOcean control panel.

**First deploy.** App Platform can't create the `match3` component until `web-build` exists,
and the upload needs the bucket. So: run the workflow on `main` once (the Web push succeeds,
the Spaces upload fails because the bucket doesn't exist yet), `terraform apply` in the infra
repo, then run the workflow again.

## One-time setup: deploy secrets

Add these in **Settings → Secrets and variables → Actions → New repository secret**, next to
the Unity ones. The Web push needs no secret: it uses the workflow's own `GITHUB_TOKEN`, which
the `deploy` job gets with write access.

- `SPACES_ACCESS_KEY_ID` and `SPACES_SECRET_ACCESS_KEY`: a Spaces access key. In the DigitalOcean
  control panel, **Spaces Object Storage → Access Keys → Create Access Key**. Limit it to the
  `games-downloads` bucket with read/write/delete access, so a leaked key can't touch the
  Terraform state bucket. The secret is only shown once.
- `DIGITALOCEAN_ACCESS_TOKEN`: an API token for the CDN purge. **API → Tokens → Generate New
  Token**, with custom scopes limited to CDN read and update, and an expiry. When it expires,
  the purge step fails with a 401. Generate a new one and replace the secret.

## How the caches work

- **Library cache.** `Library/` is Unity's import cache: every texture, script and shader converted
  for the target platform. A cold build spends most of its time filling it. With the cache,
  Unity only re-imports what changed. The key includes a hash of `Assets/`, `Packages/` and
  `ProjectSettings/`, so any change misses the exact key. `restore-keys` then falls back to the
  newest `Library-<platform>-` cache, which is almost as good.
- **LFS cache.** The PNGs and fonts are in Git LFS. The free plan allows 1 GB of LFS
  downloads a month, and each fresh runner would download all of them. The cache means they
  are downloaded again only when the set of LFS files changes.
- **Cache scope.** GitHub lets a branch read its own caches and `main`'s, but not another
  branch's, and `main` can't read a branch's. Since nothing builds by itself, **`main` only
  gets a cache when you Run workflow on `main`**. Without one, a run on a new branch is cold
  (~33 min instead of ~7-11 for Windows). So run a build on `main` now and then, for example
  after merging a batch of PRs. That also refreshes the cache other branches start from.
- **Eviction.** Caches unused for 7 days are deleted, and the repo gets 10 GB in total (each
  `Library/` cache is ~1.2-1.4 GB). Deleting a merged PR's caches (**Actions → Caches**) frees
  room so `main`'s aren't pushed out.

## Budget (private repo)

The repo is private, so Actions minutes and artifact storage count against the free quota
(2,000 Linux minutes a month on GitHub Free). A manual Windows + Web build costs ~25 min
cached (~97 cold). Nothing runs unless you start it, so that's the whole bill: PR builds
(~7-11 min cached per push, ~33 cold) and PR test runs were switched off because PRs were
usually merged before they finished.

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
