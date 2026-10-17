# Vision

> **Status: agreed.** Proposed 2026-10-10; the statement and pillars were signed off on
> 2026-10-16. The decisions at the bottom are agreed. Change any of it on purpose, in its own
> pull request.

## Statement

Match-3 is a sixty-second score chase. Every round is one minute on the game clock, every big play is
something the player built on purpose, and the only thing to beat is a score: your own, or
the world's.

It is inspired by Bejeweled 3 (and plays closest to its Lightning / Blitz side), but parity
is not a goal. We borrow what serves the one-minute round and leave the rest.

## Pillars

Use these to decide what goes in. A feature should clearly serve at least one and break none.

1. **One minute, one more round.** A round is short, starts instantly and ends with a reason
   to press Play Again. Anything that adds waiting, setup or a second screen before play has
   to justify itself.
2. **Big plays are built, not given.** Power comes from shapes the player makes on the board.
   The rules are deterministic and readable at a glance: the same shape always gives the same
   result. Luck lives only in what falls in from the top.
3. **Bigger plays feel bigger.** Sound, animation and score scale with what the player did. A
   4-match must never look or sound like a 3-match.
4. **A leaderboard worth trusting.** Everyone plays the same rules. Scores are checked by the
   server. Customisation is cosmetic only (token styles, board themes, sound sets) and never
   changes the odds.
5. **Small enough to understand.** This is a learning project. Rules live in `BoardModel`
   where they can be unit tested, every feature is an issue with a learning goal, and one deep
   mode beats several shallow ones.

## Not doing

- Parity with Bejeweled 3: Quest, Poker, Diamond Mine, Butterflies, Ice Storm.
- Level maps, lives, energy timers, purchasable boosts, or any monetisation.
- Boosts or loadouts picked before a round (breaks pillar 4).
- Rules that differ by platform.

## Decisions

- **One mode, for now** (2026-10-10). The timed round is the game. Untimed modes are not
  planned; revisit only if the single mode stops being enough.
- **The clock pauses while power gems detonate** (2026-10-10, #84). Long chains reward the
  player instead of costing them time. It keeps running during swaps, ordinary clears and
  falls.
- **The clock also pauses while the board reshuffles** (2026-10-10, #14). Running out of
  moves is luck, not a mistake, so it does not cost time.
- **Six token types** (2026-10-10, #88; to confirm by playtest). The sixth (a pink cross)
  makes accidental matches scarcer, so a power gem is more often something the player built
  (pillar 2). Every type has its own shape, so they can be told apart without colour. #88
  asked for a playtest with five types and power gems first; the sixth went in ahead of it,
  on a bot's numbers (`Docs/Scoring.md`). If rounds with six feel starved rather than earned,
  go back to five.
- **Power gems combine only when the player swaps them** (2026-10-16, #108). Two power gems
  swapped with each other make a bigger blast; gems that merely land next to each other, or
  are caught in a blast, behave as before. The biggest plays stay something the player did.
  (2026-10-17, #109) A Hypercube joins in: with a Flame or Star it turns that colour into the
  partner's power and sets them all off; two Hypercubes clear the board. Both are built, never
  dealt: a Hypercube needs a line of five.
