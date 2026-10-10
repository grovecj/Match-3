# Global leaderboard

The Leaderboard panel has two tabs. **Local** is the top 10 on this computer, as before.
**Global** is the top 10 players everywhere, from our leaderboard server
([grovecj/match-3-api](https://github.com/grovecj/match-3-api)) at
`https://games.cartergrove.me/match3/api`. Anyone can see the global board. Only signed-in
players (see [Accounts.md](Accounts.md)) post to it, and they don't type initials: the
server knows who they are from their access token, and shows their **tag** (three letters
taken from their name).

The local leaderboard works exactly as before, signed in or not. If the server can't be
reached, the Global tab says so and nothing else changes: the game never waits on it.

## How a round gets posted

```
GameManager          LeaderboardMenu       OnlineLeaderboardClient            match-3-api
 StartRound() ------> RoundStarted() -----> (signed in?) access token
                                            POST /rounds ----------------------> notes the time
                                            <------------------- { roundToken } -|
 ... about a minute of play ...
 EnterRoundOver() --> OfferEntry(score) --> POST /scores { roundToken, score } -> checks it
   (local initials prompt, if top 10)       <-------------------- { best, rank } -|
                                            "Posted! Your best: 84,300, #12"
```

The round token is the server's protection against made-up scores: it only accepts a score
for a round it saw start at least 55 seconds earlier, once per round, and no more than 10,000
points per second of play. So a round must really be played to be posted.

**Game time vs. wall-clock time:** the round clock is *game* time. It stops while the board
reshuffles and while power gems detonate (see `RoundClock` and `Board.IsDetonating`), so a
round that shows 60 seconds can take noticeably longer than a minute of real time. That's
fine for the server: the 55-second check is a *lower* bound, and the only upper bound is the
round token's expiry (`match3.rounds.expiry`, 1 hour in match-3-api's `application.yaml`).
The score and swap caps are worked out from the configured round length (60 s), not from how
long the round really took, so a longer round doesn't change them. If a new rule ever holds
the clock for minutes at a time, that expiry is the number to check.

Achievements ride on the same request: the score is posted with the round's stats (how many
matches of each size, and so on), and the reply lists what the round unlocked. See
[Achievements.md](Achievements.md).

**Testing tip:** that also means a shortened round (a small `roundDuration` on the
GameManager) is turned down with "The global leaderboard didn't accept this round." Test
posting with full 60-second rounds.

**Idle connections:** `UnityWebRequest` reuses connections, and Cloudflare (in front of the
server) closes one that has been idle for about a minute: exactly a round's length. The score
would then go out on a dead connection and fail without reaching the server (Unity reports
"Unknown Error"). So `HttpLeaderboardApi` retries a request once when the *connection* fails,
on a fresh one. If the retried score post is told the round already has a score, the first
try did arrive, and it counts as posted. Browsers retry by themselves, so this only matters
in the Editor and the Windows build.

## The pieces

All in `Assets/Scripts/Online/` (namespace `Match3.Online`):

| Class | Does |
| --- | --- |
| `ILeaderboardApi` | The four requests (`POST /rounds`, `POST /scores`, `GET /scores/top`, `GET /me`) as coroutines, plus the result types. |
| `HttpLeaderboardApi` | The real one, with `UnityWebRequest`. Sends `Authorization: Bearer <access token>`. |
| `LeaderboardJson` | Reads the replies with `JsonUtility` (and works around its limits: no top-level arrays, no `null` numbers). |
| `OnlineLeaderboardClient` | The MonoBehaviour that runs it all: registers rounds, posts scores, loads the board, and keeps the messages the panel shows. Raises `Changed`. |

The same server keeps signed-in players' settings; see [SettingsSync.md](SettingsSync.md).

`LeaderboardMenu` owns the tabs and asks the client for what to show. **Match3 > Build
Leaderboard Menu** (`Assets/Editor/LeaderboardMenuBuilder.cs`) builds the tabs and the status
line, and puts the `OnlineLeaderboardClient` next to the `LeaderboardMenu`, wired to the
scene's `AccountManager` (so build the Account menu first).

## Why the browser build needs no CORS

The web build is served from `https://games.cartergrove.me/match3/` and the API from
`https://games.cartergrove.me/match3/api`: the same origin (scheme + host + port), so the
browser allows the requests without asking the server first. (The accounts server, on
`auth.cartergrove.me`, is a different origin and does have to allow ours.)

A web build served locally (`http://localhost:8000`) is a different origin, so the browser
blocks its calls to the real API: the Global tab says it couldn't reach the leaderboard. Test
the global board in the Editor, in the Windows build, or on the real site.

## Tests

`LeaderboardJsonTests` (Edit Mode) feeds the parser replies in the server's exact shapes, plus
an HTML page and empty bodies. `OnlineLeaderboardClientPlayModeTests` (Play Mode) runs the
client against a fake server: posting with the round's token, staying quiet when signed out,
failing politely when the server or the accounts server is down or the score is turned down,
and loading the board.
