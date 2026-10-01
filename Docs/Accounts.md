# Accounts (sign in with cartergrove.me)

Players can sign in with their cartergrove.me account (a Google login) from the **web build**
and the **Windows build** (and the Editor).
Signing in is optional: the game, the local leaderboard and settings all work without it, and
if the accounts server is down the game says so and carries on.

The accounts server is [grovecj/accounts](https://github.com/grovecj/accounts), an OAuth 2.1 /
OpenID Connect provider at `https://auth.cartergrove.me`. The game is registered there as two
**public clients** (no client secret; see PKCE below):

| Client id        | Build   | Redirect URI                              |
|------------------|---------|-------------------------------------------|
| `match3-web`     | Web     | `https://games.cartergrove.me/match3/`    |
| `match3-desktop` | Windows, Editor | `http://127.0.0.1:<any port>/callback` |

Both clients are the same accounts: the player's id (the token's `sub`) is their account on
the server, not something per client, so signing in with the same Google account on the web
and on Windows makes you the same player. Refresh tokens do belong to the client that got
them, which is why `AccountManager` refreshes and revokes with the client id of its own build.

## The code

Everything lives in `Assets/Scripts/Accounts/` (namespace `Match3.Accounts`):

| File | What it does |
|------|--------------|
| `AuthConfig` | The issuer, client ids, endpoint URLs, and building the `/oauth2/authorize` URL. |
| `Pkce` | Makes the PKCE code verifier and its S256 challenge, and random `state` values. Also `Base64Url`. |
| `AuthCallback` | Reads `?code=...&state=...` from the page URL, checks `state`, and strips those parameters again. |
| `IAuthFlow` | "Get me an authorization code." One implementation per platform, plus a fake in tests. |
| `WebRedirectAuthFlow` + `Assets/Plugins/WebGL/Auth.jslib` | The browser's flow (only compiled into Web builds). |
| `LoopbackAuthFlow` | The Windows (and Editor) flow: a tiny web server on `127.0.0.1` and the system browser. Not in Web builds. |
| `ITokenClient` / `HttpTokenClient` | POSTs to `/oauth2/token` (code exchange, refresh) and `/oauth2/revoke`, with `UnityWebRequest`. |
| `JwtClaims` | Reads the player's id and name out of the access token (a JWT). |
| `TokenStore` | The refresh token and profile under `Account.*`: localStorage in a browser, PlayerPrefs elsewhere. |
| `AccountManager` | The SignedOut / SigningIn / SignedIn state machine, refreshing, sign-out. Raises `Changed`. |
| `AccountMenu` | The Account button and window (with Cancel while waiting for the browser). Built by **Match3 > Build Account Menu** (`Assets/Editor/AccountMenuBuilder.cs`). |

Other code that needs to call our servers as the player (the global leaderboard, #25) asks the
`AccountManager` for a token:

```csharp
yield return accountManager.GetAccessToken(token =>
{
    // token is null if nobody is signed in or the accounts server can't be reached.
});
```

## Signing in on the web

The authorization code flow with PKCE, by redirecting the game's own tab:

```
Game (tab)                                   auth.cartergrove.me                  Google
   | 1. Sign in: make verifier + state,
   |    save them in sessionStorage
   |--- 2. navigate to /oauth2/authorize?client_id=match3-web&code_challenge=...&state=... --->|
   |                                              |--- 3. "Sign in with Google" ---------------->|
   |                                              |<-- player signs in -------------------------|
   |<-- 4. redirect to /match3/?code=...&state=... |
   | 5. Game loads again. Check state matches,
   |    remove ?code from the address bar
   |--- 6. POST /oauth2/token (code + verifier) -->|
   |<-- access token (JWT, 15 min) + refresh token (30 days)
```

1. **PKCE.** A game can't keep a client secret (anyone can dig it out of the build). Instead,
   each sign-in makes a random *code verifier* and sends only its SHA-256 hash (the
   *challenge*) in step 2. In step 6 the game proves it started this sign-in by sending the
   verifier itself. A stolen code is useless without it.
2. **Same tab.** `Application.OpenURL` would open a new tab, so the `.jslib` sets
   `window.location` instead. The game unloads; `AccountManager.Start` picks things up when the
   page loads again (`IAuthFlow.TryResume`).
3. **state.** A random value saved before leaving and echoed back by the server. A callback
   with a missing or different state is rejected (it could be an attacker's link that would
   sign the player in as the attacker). The verifier and state are kept in `sessionStorage`,
   not PlayerPrefs: in a browser `PlayerPrefs.Save()` reaches IndexedDB asynchronously, and the
   page is navigated away straight after, so they could be lost.
4. **Tidy up.** The code is single-use, so `history.replaceState` takes `?code=...&state=...`
   off the address bar; a reload or a bookmark won't try it again.

## Signing in on Windows

A desktop game can't be redirected to like a web page, so it does what native apps do (RFC 8252,
"OAuth 2.0 for Native Apps"): it briefly runs its own tiny web server and uses that as the
redirect URI.

```
Game                                  Player's browser            auth.cartergrove.me
 | 1. Sign in: start HttpListener on
 |    http://127.0.0.1:<free port>/callback/,
 |    make verifier + state
 |--- 2. Application.OpenURL(/oauth2/authorize?client_id=match3-desktop
 |        &redirect_uri=http://127.0.0.1:<port>/callback&code_challenge=...&state=...) -->|
 |                                      |--- 3. sign in with Google -------->|
 |                                      |<-- 4. redirect to 127.0.0.1:<port>/callback?code=...&state=...
 |<-- 5. GET /callback?code=...&state=...
 |--- "Signed in. You can close this tab." -->|
 | 6. Check state, stop the server
 |--- 7. POST /oauth2/token (code + verifier) ------------------------------->|
```

1. **The system browser**, not a window inside the game: the player sees the real address bar,
   may already be signed in to Google there, and the game never sees their password. (Google
   blocks sign-in from embedded web views for this reason.)
2. **127.0.0.1, any free port.** The game asks Windows for a free port each time, so it can't
   clash with anything. The accounts server accepts any port for a loopback redirect (RFC 8252
   §7.3). 127.0.0.1 rather than `localhost`, which is only a name and could resolve elsewhere.
3. **Back to the main thread.** `HttpListener.GetContextAsync()` waits for the browser on a
   background thread, but almost all of Unity only works on the main thread. So the flow is a
   coroutine (main thread) that checks once a frame whether the request has arrived, and reads
   and answers it there.
4. **state** works as on the web. Any web page could send the browser to our port, so a request
   without our state gets a 400 and the game keeps waiting for the real one; anything that isn't
   a callback at all gets a 404.
5. **Giving up.** After 3 minutes, or when the player presses **Cancel** in the Account window,
   the flow stops the server (freeing the port) and the game goes back to signed out with a
   message. Closing the game, or leaving Play Mode in the Editor, also stops it.

**The firewall.** The server listens on 127.0.0.1 only, so nothing outside this computer can
reach it, and the Windows Defender Firewall doesn't ask "allow Match-3 to communicate on
networks?" (it only asks for programs that accept connections from the network). To check: on
a Windows build that has never been allowed or blocked before, sign in, and no firewall prompt
should appear.

Like on the web, only the refresh token and profile are saved (PlayerPrefs, which is the
registry on Windows), so restarting the game keeps you signed in. The Editor and the Windows
build keep separate PlayerPrefs, so signing in in one doesn't sign you in to the other.

## Staying signed in

Only the **refresh token** (and the player's id and name) is saved: in the page's
`localStorage` in a browser, PlayerPrefs elsewhere. (Not PlayerPrefs in the browser, for the
same reason as above: a reload just after a refresh could lose the rotated token and bring back
the retired one, which the server would treat as theft.) On the next
visit the game is signed in straight away with the saved name, and refreshes in the background
for a new access token. The access token is kept in memory only.

The server **rotates** refresh tokens: every refresh returns a new one and retires the old one,
and reusing a retired one revokes the whole chain (it looks like theft). So the new token is
saved every time, and two refreshes must never overlap: not within one game (a `refreshing`
flag), and not across browser tabs, which share the saved token. For tabs, `TokenStore` has a
*refresh lease*: a tab writes its id and an expiry (30s) to `Account.RefreshLease`, checks a
frame later that it's still its own (if two tabs write at once, the last write wins), then
re-reads the refresh token (another tab may just have rotated it), refreshes, saves, and
releases the lease.

| Refresh result | What happens |
|----------------|--------------|
| Success | New access token; the rotated refresh token and name are saved. |
| `invalid_grant` (expired, revoked, already used) | Signed out, with "Your sign-in has expired". |
| Anything else (network, 5xx, rate limit, other OAuth errors) | Still signed in; a message says the server can't be reached. |

**Sign out** forgets everything locally and asks `/oauth2/revoke` to revoke the refresh token.
If that request fails, the token is already gone from the game and expires on its own.

## Where it's available

- **Web build:** yes, with `WebRedirectAuthFlow`.
- **Windows build and the Editor:** yes, with `LoopbackAuthFlow`. (The Editor can't run a
  `.jslib`, so even with the Web build target selected it uses the loopback flow.)
- Like Settings, the Account button is only shown between rounds. A sign-in that's waiting for
  the browser carries on if a round starts.

## Testing against a local accounts server

### Windows / the Editor

1. Run grovecj/accounts locally, and set **Issuer** on the Game scene's **AccountManager** to
   it (don't commit that). `match3-desktop` already accepts any `127.0.0.1` port.
2. Enter Play Mode (or make a Windows build) and click **Account > Sign in with Google**. Your
   browser opens; sign in, and the tab should say "Signed in". Back in the game, the Account
   button shows your name.

Things to try:

- **Stay signed in:** stop and start Play Mode (or restart the build).
- **Cancel:** start signing in, then press **Cancel** in the Account window instead of finishing
  in the browser. "Sign-in was cancelled." The browser tab now can't reach the game.
- **Timeout:** start signing in and wait 3 minutes.
- **Forged callback:** while it's waiting, copy the port from the browser's address bar
  (`redirect_uri`) and open `http://127.0.0.1:<port>/callback?code=x&state=forged` in another
  tab: it's turned away, and the real sign-in still works.

### Web

1. Run grovecj/accounts locally (see its README). Note its URL, e.g. `http://localhost:9000`.
2. Register a redirect URI for `match3-web` that matches where you'll serve the web build,
   e.g. `http://localhost:8000/` (the server only accepts exact, registered redirect URIs).
   The game uses the page's own URL, minus any query, as its redirect URI.
3. The server has to allow that page's origin with CORS on `/oauth2/token` and
   `/oauth2/revoke` (production only allows `https://*.cartergrove.me`).
4. In the Game scene, select **AccountManager** and set **Issuer** to your local server.
   **Don't commit that change.**
5. **Match3 > Build Web**, then serve `Builds/Web` from the URL in step 2 (see
   [WebBuild.md](WebBuild.md#making-a-build)) and click **Account > Sign in with Google**.

Things to try:

- **Forged callback:** open `http://localhost:8000/?code=anything&state=forged` directly (an
  "attacker's link": no sign-in was started in this tab). The Account window should open saying
  the response didn't match, and the address bar should go back to `http://localhost:8000/`.
- **Missing state:** the same with `?code=anything` and no `state`.
- **Stay signed in:** reload the page. The name should still be on the Account button.
- **Offline:** stop the accounts server and reload. You stay signed in, with a message.
- **Revoked:** sign out in another tab (or revoke in the server's database), then reload. The
  game signs you out and says your sign-in has expired.

Automated tests: `PkceTests` and `AuthCallbackTests` (Edit Mode) cover PKCE (including the RFC
7636 test vector), URL parsing and the state check; `AccountManagerPlayModeTests` (Play Mode)
covers the state machine with a fake `IAuthFlow` and a fake token server; and
`LoopbackAuthFlowPlayModeTests` (Play Mode) runs the real loopback server and plays the
browser's part with HTTP requests: success, a forged state, the player saying no, Cancel, the
timeout, and the port being freed afterwards.
