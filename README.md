# Match-3

[![Build](https://github.com/grovecj/Match-3/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/grovecj/Match-3/actions/workflows/build.yml)

A Bejeweled-style match-3 game built in Unity, primarily as a learning project for
Unity game development and AI-assisted workflows.

## Play

- **In the browser:** <https://games.cartergrove.me/match3/>
- **Windows download (latest release):**
  [Match3-Windows-latest.zip](https://games-downloads.nyc3.cdn.digitaloceanspaces.com/match3/Match3-Windows-latest.zip).
  Unzip it and run `Match3.exe`.

## Requirements

- Unity **6000.5.7f1** (install via Unity Hub)
- Visual Studio 2022 with the *Game development with Unity* workload
- Git + Git LFS (`git lfs install` once per machine)
- Blender (optional, for token art in `Art/`)

## Getting started

1. Clone the repo: `git clone https://github.com/grovecj/Match-3.git`
2. In Unity Hub: **Add → Add project from disk** and select the repo root.
3. Open `Assets/Scenes/SampleScene.unity`.

## Layout

| Path | Contents |
| --- | --- |
| `Assets/` | Everything Unity imports: scenes, scripts, sprites, prefabs |
| `Packages/`, `ProjectSettings/` | Unity package manifest and project settings |
| `Art/` | Source art (e.g. `.blend`) not imported directly by Unity |

## Releasing

Releases are manual. Merging to `main` doesn't deploy anything.

1. Go to **Actions → Build → Run workflow**, pick `main`, leave **Deploy** ticked, and run it.
2. The workflow builds Windows and Web. If both succeed, its **Deploy** job:
   - force-pushes the Web build to the `web-build` branch. DigitalOcean App Platform watches
     that branch and redeploys <https://games.cartergrove.me/match3/> a few minutes later.
   - zips the Windows build and uploads it to the `games-downloads` Spaces bucket as
     `match3/Match3-Windows-<short-sha>.zip` and `match3/Match3-Windows-latest.zip`, then
     purges the CDN's cached copy of "latest".
3. The run's summary page links to the game and both zips.

To **redeploy** or **roll back**, run the workflow again on `main` (or, for a rollback, first
revert the bad change on `main`). Only runs on `main` deploy; runs on other branches just build.
Pull requests never deploy. Untick **Deploy** to build `main` without releasing it.

**Where things live:**

| What | Where |
|---|---|
| Workflow | [`.github/workflows/build.yml`](.github/workflows/build.yml), explained in [`Docs/CI.md`](Docs/CI.md) |
| DigitalOcean resources (app, domain, bucket, CDN) | Terraform in [grovecj/cartergrove-me-infra](https://github.com/grovecj/cartergrove-me-infra) (`projects/games/`), not this repo |
| Secrets | This repo's **Settings → Secrets and variables → Actions**: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` (builds), `SPACES_ACCESS_KEY_ID`, `SPACES_SECRET_ACCESS_KEY`, `DIGITALOCEAN_ACCESS_TOKEN` (deploy). Setup steps are in [`Docs/CI.md`](Docs/CI.md#one-time-setup-deploy-secrets). |

## Task tracking

Work is tracked in [GitHub Issues](https://github.com/grovecj/Match-3/issues).
