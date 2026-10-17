# Achievements

Signed-in players earn achievements ("Play 50 rounds", "Match 5 tokens in a row"). An unlock
slides in as a toast, and the **Achievements** button in the side panel lists them all with
progress. Guests see the same list, locked, under "Sign in to earn achievements".

Two ideas shape the whole feature:

- **The server decides.** The game never says "unlock this", and the server has no request for
  it. The game reports each round (as it already did for the leaderboard), and the server works
  out what that earned. A cheater can't simply ask for an achievement.
- **Achievements are data.** What they are (name, description, icon, rule) is a list in the
  server's configuration. The game knows none of them by name: it shows whatever list the server
  sends, so adding one needs no new build of the game.

The server side is in [grovecj/match-3-api](https://github.com/grovecj/match-3-api); its README
has the rules and the request details.

## How a round becomes an achievement

```
Board events ──> RoundStatsTracker ──> OnlineLeaderboardClient ──POST /scores──> server
 (swap, match,     counts them into      sends score + stats        checks them, stores the
  clear step)      a RoundStats          when the round ends         round, awards achievements
                                                                              │
AchievementToast <── Unlocked ── AchievementsClient <── ScorePosted <── {best, rank, unlocked}
```

1. `RoundStatsTracker` listens to the `Board`'s events, like `ScoreKeeper` does, and keeps a
   `RoundStats`: swaps, matches of 3 / 4 / 5+, the longest match, the best chain (the most
   clears one swap set off, cascades included), power gems made and set off by kind, the most
   gems one swap cleared, the combos swapped, and how the score was built up (see
   [Scoring.md](Scoring.md)).
2. When the round ends, `OnlineLeaderboardClient` posts the stats with the score.
3. The server checks that the stats fit together (the matches and blasts, at their
   multipliers, must add up to exactly the score: see [Scoring.md](Scoring.md)),
   stores the round, and compares the player's totals with every achievement's goal. Whatever is
   newly earned comes back in the reply as `unlocked`.
4. `OnlineLeaderboardClient` raises `ScorePosted`; `AchievementsClient` raises `Unlocked` for
   each one (never twice), and `AchievementToast` shows them one after another.

The stats come from the player's machine, like the score, so they could still be forged; the
server's checks just mean a forgery has to be a consistent one. The API's README is frank about
that.

If the server's reply to the score is lost (see "Retry" in [GlobalLeaderboard.md](GlobalLeaderboard.md)),
the game doesn't know what was unlocked. `AchievementsClient` then loads the list again and
announces whatever is unlocked now that wasn't before.

## The contract shared with other games

games.cartergrove.me will host more games, and the hub will show a player's achievements from
all of them together. So the two read requests answer in a shape that isn't about Match-3 at
all, and every game's API on the hub is meant to answer the same way:

```
GET /<game>/api/achievements       public: everything there is to earn
GET /<game>/api/me/achievements    signed in: the same list with your progress
```
```json
{ "game": "match3",
  "achievements": [
    { "key": "rounds-50", "name": "Veteran", "description": "Play 50 rounds", "icon": "rounds",
      "goal": 50, "progress": 23, "unlockedAt": null }
  ] }
```

`key` never changes; `progress` runs from 0 to `goal` (a one-off achievement has `goal` 1);
`unlockedAt` is a UTC time once earned. `icon` is only a name: each client has its own pictures.
The full rules are under "The achievements contract" in the API's README.

How a game *earns* achievements is its own business (here, round stats). Only reading them is
shared.

## Metrics

The server turns the stats into *metrics*, the numbers an achievement's goal is measured in.
Most add up over every round; a "best" one is the best single round.

| Metric | From the `RoundStats` field | Kind |
|---|---|---|
| `flames-created` / `stars-created` / `hypercubes-created` | `flamesCreated` / `starsCreated` / `hypercubesCreated` | total |
| `most-gems-one-swap` | `mostGemsOneSwap` | best round |
| `combos` | `combos` | total |

(The API's README lists the older ones too: rounds, score, runs, chains.)

`combos` counts the player's swaps that put two power gems together (issues #108 and #109):
the Board raises `ComboSwapped` once for such a swap, and `RoundStatsTracker` counts it. Power
gems that go off any other way, the Last Hurrah's included, never count.

### Adding a field without breaking older games

`combos` came after scoring version 2 and didn't get a version of its own: it isn't part of the
score, so nothing about checking the score changes. Instead the server treats it as optional. A
round without it (from an older build of the game) counts as 0 combos and is still accepted; a
round with it is checked for plausibility (`0 <= combos <= swaps`, and every combo sets off at
least two power gems). The server goes out first, so it is already storing and checking
`combos` when the first game that sends it is released.

## The pieces in this project

Everything in `Assets/Scripts/Achievements/` speaks only the contract and knows nothing about
boards or matches:

| Script | Job |
|---|---|
| `Achievement` | One item of the list, as the server sends it |
| `AchievementsJson` | Reads the reply |
| `IAchievementsApi` / `HttpAchievementsApi` | The two requests (tests use a fake) |
| `AchievementsClient` | Holds the list, reloads it, and announces unlocks |
| `AchievementsMenu` / `AchievementRow` | The window and one line of it |
| `AchievementToast` | The "Achievement unlocked" card |
| `AchievementIcons` | Icon name -> sprite, with a fallback |

The Match-3 part is small: `RoundStats` and `RoundStatsTracker` (in `Assets/Scripts/`), and the
stats travelling with the score in `Assets/Scripts/Online/`.

## Building the UI

**Match3 > Build Achievements Menu** (`Assets/Editor/AchievementsMenuBuilder.cs`) creates the
button, the window, the toast, the `AchievementRow` prefab, the `AchievementIcons` asset, and
the `AchievementsClient` and `RoundStatsTracker`, and wires them up. Save the scene afterwards.
Run **Build Account Menu** and **Build Leaderboard Menu** first; running Build Leaderboard Menu
again later re-connects the achievements pieces by itself.

The GameManager's Achievements Menu field is the one menu reference that may be empty, so the
game still runs in a scene where this hasn't been built.

Some Unity things this feature introduces, each explained where it's used:

- **Scroll Rect** (viewport + content) for a list longer than its window, filled at run time by
  `Instantiate`-ing a prefab per row (`AchievementsMenuBuilder.BuildWindow`, `AchievementsMenu.Refresh`).
- **Anchors as a progress bar**: the fill's right-hand anchor is moved to the fraction done
  (`AchievementRow.Show`).
- **Canvas Group** to fade a whole row at once.
- **Unscaled time** (`Time.unscaledDeltaTime`, `WaitForSecondsRealtime`) so the toast animates
  even if the game is paused (`AchievementToast`).

## Icons

`Art/Icons/build_achievement_icons.py` draws them into `Assets/Sprites/Achievements/`, white on
transparent so the UI can tint them. The file name is the icon name. After adding one, run
Build Achievements Menu again so `AchievementIcons` picks it up. An icon name the game has no
sprite for gets the trophy.

## Adding an achievement

1. In match-3-api, add an entry to `match3.achievements` in `application.yaml` (key, name,
   description, icon, metric, goal) and deploy. That's all if an existing metric and icon fit.
2. A new *kind* of number needs a `Metric` there, and, if the game doesn't report it yet, a new
   field in `RoundStats` here and in the API (plus a check that it's plausible).

## Sound

The toast plays `SoundId.AchievementUnlocked`. In the Chimes set it's the same clip as a
5-match for now; pick another in `Assets/Data/SoundSets/Chimes.asset`.

## Tests

- Edit Mode: `RoundStatsTests`, `AchievementsJsonTests`, and the `unlocked` cases in
  `LeaderboardJsonTests`.
- Play Mode: `AchievementsClientPlayModeTests` (fake server), the stats and `ScorePosted` cases
  in `OnlineLeaderboardClientPlayModeTests`, and `RoundStatsTracker_CountsWhatTheBoardReports`
  in `BoardSwapPlayModeTests`.
- The API has its own (`AchievementsTests`).
