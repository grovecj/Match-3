# Scoring

How a round's score is worked out, and what the server checks it against. The game
(`Assets/Scripts/ScoreRules.cs`) and the API ([grovecj/match-3-api](https://github.com/grovecj/match-3-api))
must agree on every number here, so this page is the one place both sides are listed. Change a
rule in both, and here, together.

## The rules (version 2)

All numbers are starting values, to tune in playtests (#85).

| What | Points |
|---|---|
| A run of 3 | 1,000 |
| A run of 4 | 2,000 |
| A run of 5 or more | 5,000 |
| Each gem a blast destroys (a Flame, Star or Hypercube going off), if it wasn't part of a run | 250 |
| Making a power gem | nothing extra: the bigger run already pays for it |

**Cascade multiplier.** Everything scored in one clear step is multiplied by
(cascade index + 1), capped at **x5**: the player's own match is x1, the first cascade x2,
and so on. Every wave of a chain reaction is inside one step, so they share its multiplier.
A swapped Hypercube's step is step 0 (x1), and the first cascade after it is x2. The
multiplier goes back to x1 on the next swap. The side panel shows it ("x3") while it's above x1.

**Last Hurrah.** When the clock reaches 0:00 (and any cascade in progress has finished), every
power gem still on the board goes off, row by row from the bottom-left, and scores by the same
rules. Each gem set off this way is a step 0 of its own (x1), like a swapped Hypercube, with
its whole chain reaction inside it; a Hypercube picks the most common colour, as one caught in
a blast does. The cascades after the fall are x2, x3 and so on. If one of them makes a new
power gem, that goes off too, starting again from x1. The round-over screen and the score
submission wait for all of it. No new rule or number is involved, so the scoring version and
the server's checks are unchanged: hurrah points arrive in `blastedGems`, the run counts and
`basePointsAtMultiplier` like any others.

**Combos** (#108). Swapping a Flame or Star with an adjacent Flame or Star sets off one bigger
blast, centred on the cell the moved gem lands in: the 5x5 for two Flames, three whole rows and
three whole columns for any pair with a Star. It scores like a swapped Hypercube: it is step 0
(x1), every other gem it destroys is a blasted gem (250), and the two combo gems themselves
score nothing, just as a swapped Hypercube's own cell doesn't. Power gems it catches go off in
the next wave as usual. No new rule or number is involved, so `ScoreRules.Version` stays at 2
and the server's checks are unchanged; each combo gem counts as detonated by its own kind.

So a run of 3 whose Flame destroys 8 more gems is worth 1,000 + 8 x 250 = 3,000 as the
player's own match, and 6,000 on the first cascade.

### How the code is laid out

```
Board ──ClearStepScored(ClearStep)──> ScoreKeeper ──Changed / MultiplierChanged──> ScoreView
   once per step, after its              ScoreRules.PointsForStep(runs, blasted, cascadeIndex)
   chain reaction, before the fall
```

- `ScoreRules` is the arithmetic and nothing else: constants and pure static methods, tested
  in `ScoreRulesTests` without a Board or a scene. The numbers are constants, not Inspector
  fields, on purpose: a value tuned in one scene would make the game and the server disagree.
- `ClearStep` is the summary of one step: its runs, its blasted gems, its cascade index (and
  how many gems left the board, for the stats). `ClearStep.From` builds it from the model's
  `ClearResult`.
- `ScoreKeeper` adds each step and keeps the multiplier; `ScoreView` shows both.

## What the server checks

The round's stats go with its score (see [Achievements.md](Achievements.md)). For scoring,
these fields matter:

| Field | Meaning |
|---|---|
| `scoringVersion` | Which rules the round was played by. `2` for these. Rounds without it were version 1 (runs only, no multiplier). |
| `runs3`, `runs4`, `runs5` | Runs of 3, 4, and 5 or more |
| `blastedGems` | Gems destroyed by blasts, not runs |
| `basePointsAtMultiplier` | Five numbers: points scored at x1, x2, x3, x4 and x5, *before* the multiplier |

With them the server can check the score exactly:

```
sum(basePointsAtMultiplier) == runs3*1000 + runs4*2000 + runs5*5000 + blastedGems*250
score == basePointsAtMultiplier[0]*1 + [1]*2 + [2]*3 + [3]*4 + [4]*5
```

The other new stats are for achievements (#71), not for checking the score:
`flamesCreated`, `starsCreated`, `hypercubesCreated`, `flamesDetonated`, `starsDetonated`,
`hypercubesDetonated` and `mostGemsOneSwap` (every gem one swap cleared, cascades included;
gems cleared by the Last Hurrah belong to no swap, so they're left out of it).
The longest cascade is `bestChain` - 1: `bestChain` already counts the steps of the longest
chain, the player's own match (or swapped Hypercube) included.

## Limits

| Limit | Game | Server (match-3-api) |
|---|---|---|
| Round length | 60 s (`GameManager.roundDuration`) | 60 s (its configured round length) |
| Most points per second of play | n/a | **version 1:** 10,000 (so `maxScore` 600,000). **Version 2:** 40,000 (so `maxScore` 2,400,000) |
| Multiplier cap | `ScoreRules.MaxMultiplier` = 5 | 5 |
| Points per run / per blasted gem | `ScoreRules` | the same values, for the check above |

Why 40,000 a second: a single step can now be worth far more than before (a board-clearing
chain at x5 is tens of thousands of points on its own), and the exact check above already
stops a made-up mix of stats. The cap is only there to turn away the absurd, so it's set
generously. Revisit it once real version-2 rounds are on the board.

## Six token types (#88)

The board went from five token types to six on 2026-10-10. No scoring rule or number changed,
so `ScoreRules.Version` stays at 2 and the server's checks are untouched. What changed is how
often things happen. To put numbers on it, a bot played 40,000 random valid swaps on each
board through `BoardModel` and `ScoreRules` (no clock, no skill, and it never saves a power
gem for later, so read these as ratios, not as what a player scores):

| Per swap (random bot) | Five types | Six types |
|---|---|---|
| Points | 5,590 | 2,930 |
| ... of which from cascades, not the swap's own match | 74% | 57% |
| Clear steps | 1.83 | 1.47 |
| Power gems made per 100 swaps | 26 | 12 |
| ... of which by the swap's own match | 33% | 45% |
| Valid moves to choose from | 15.6 | 9.4 |
| Boards left with no move (reshuffles) per 1,000 swaps | 0 | 1 |

So scores roughly halve for the same play, and nearly all of the loss is points the board
used to hand out by itself: the swap's own match is worth about the same (1,450 against
1,250). That is what the sixth type is for (see `Docs/Vision.md`, pillar 2).

**Point values: unchanged (proposed).** The values set what a play is worth relative to
another play, and that hasn't changed; only the size of the totals has. Scaling every number
up to get the old totals back would need a new scoring version and a server release for no
change in how the game plays. Tune them from real rounds instead, with the rest of #85's
starting values.

**Server cap: unchanged.** `maxScore` is a ceiling, and six types only lowers what can be
scored under it, so the limits below still hold. They can be tightened once real six-type
rounds are in.

**Old scores: keep them (proposed).** The scores on the global leaderboard were set with five
types but under version 1 (runs only, no multiplier, nothing for blasted gems). For the same
bot, five types under version 1 pay about 2,500 a swap and six types under version 2 about
2,930, so the old scores are still not inflated and the decision at the bottom of this page
stands. The one exception is a local leaderboard holding version-2 rounds played on five
types (between #85 and #88): those were set on an easier board. They're on the player's own
machine only, so they're left alone; delete the local leaderboard file to start level.

## Releasing a rule change

1. Change the rule in `ScoreRules` and raise `ScoreRules.Version`.
2. Change the API to accept the new version (its checks and caps, per version), and **deploy
   it first**. An old API turns down rounds it can't add up, so a new game released first
   would have its scores rejected.
3. Release the game.
4. Update this page.

The API change for version 2 is tracked in match-3-api, not here.

## Scores set under the old rules

**Decision (proposed, 2026-10-10): keep them.** Version-1 scores stay on the global and local
leaderboards. They were set by real play under rules that only ever paid less, so they're
not inflated, and any version-2 round that plays well will pass them. Starting a new season
would need season support in the API and an empty board for a while, for little gain. If the
old scores turn out to crowd the top list, a season can still be added later.
