# Round summary and personal best

Every round ends by saying how it went (issue #107): the score against your personal best, and
a few lines about what you built. During a round, a "Best 84,000" line under the score shows
what there is to beat, and turns into a gold "New best!" as soon as the score passes it.

## What it shows

| When | Under the score | Round summary card |
|---|---|---|
| First round ever | nothing | `84,200` |
| Beat the best | "New best!" (gold) once passed | `84,200: New best!` (gold) |
| Equal to the best | "Best 84,000" | `84,000: equals your best` |
| Missed it | "Best 84,200" | `71,800, 12,400 short of your best` |

Under the score line, the card has the stat lines, from the same `RoundStats` that are posted to
the server for that round:

```
Flames 3 · Stars 1 · Hypercubes 0
Best chain: 4
Most gems from one swap: 23
```

The side panel's message says "Time's up!". The card sits over the board and never takes a
click, so Play Again works straight away; the local top-10 initials prompt still opens over it,
and the card is still there when the prompt closes. A signed-in player's "Posted! Your best: X,
#rank" line (the server's best) is unchanged.

## Rules

- **A tie is not a new best.** `PersonalBest.Beats` is the one place that says so; the
  summary and the in-round line both use it.
- **The comparison is made after the Last Hurrah**, so its points count.
- **One best per device**, for guests and signed-in players alike, kept in PlayerPrefs under
  `personalBest.v2.6types` (the registry on Windows, IndexedDB in a browser). It started fresh
  with six token types: scores on the local leaderboard from before are never read for it.

## Code

- `PersonalBest` (plain C#) holds the best and records finished rounds. Where the number is
  kept is behind `IPersonalBestStore`: `PlayerPrefsPersonalBestStore` in the game, a fake in
  the tests.
- `RoundSummary.Create(score, previousBest, stats)` is a pure function that returns the lines
  to show. New stat lines go in `StatLinesFor`.
- `GameManager.ShowRoundSummary` (on entering RoundOver) reads the best, records the round,
  and hands the summary to `RoundSummaryView`. `ScoreView.ShowBest` sets the in-round line;
  the GameManager calls it when a round starts, so a new best keeps saying "New best!" until
  the next round.
- Tests: `PersonalBestTests` and `RoundSummaryTests` (Edit Mode).

## Building it

**Match3 → Build Round Summary** makes the `BestLine` text in the side panel and the
`RoundSummary` card over the board, and wires them to the `ScoreView`, the `GameManager` and the
`RoundStatsTracker`. Run it after **Build Achievements Menu** (which makes the tracker); it
re-wires the `ResponsiveLayout` itself, which moves the best line and the card for portrait.
Save the scene afterwards.

Without the card (builder not run), the whole summary goes in the side panel's message instead.
