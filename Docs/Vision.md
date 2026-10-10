# Vision

> **Status: draft.** Proposed 2026-10-10. The decisions at the bottom are agreed; the
> statement and pillars have not been explicitly signed off yet.

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
- **Five token types until power gems are playtested** (2026-10-10). A sixth colour is
  ticketed as #88 and deferred.
