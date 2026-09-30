# Accounts (sign in with cartergrove.me)

Players can sign in with their cartergrove.me account (a Google login) from the **web build**.
Signing in is optional: the game, the local leaderboard and settings all work without it, and
if the accounts server is down the game says so and carries on.

The accounts server is [grovecj/accounts](https://github.com/grovecj/accounts), an OAuth 2.1 /
OpenID Connect provider at `https://auth.cartergrove.me`. The game is registered there as two
**public clients** (no client secret; see PKCE below):

| Client id        | Build   | Redirect URI                              |
|------------------|---------|-------------------------------------------|
| `match3-web`     | Web     | `https://games.cartergrove.me/match3/`    |
| `match3-desktop` | Windows | `http://127.0.0.1:<any port>/callback` (a later issue) |

## The code

Everything lives in `Assets/Scripts/Accounts/` (namespace `Match3.Accounts`):

| File | What it does |
|------|--------------|
| `AuthConfig` | The issuer, client ids, endpoint URLs, and building the `/oauth2/authorize` URL. |
| `Pkce` | Makes the PKCE code verifier and its S256 challenge, and random `state` values. Also `Base64Url`. |
| `AuthCallback` | Reads `?code=...&state=...` from the page URL, checks `state`, and strips those parameters again. |
| `IAuthFlow` | "Get me an authorization code." One implementation per platform, plus a fake in tests. |
| `WebRedirectAuthFlow` + `Assets/Plugins/WebGL/Auth.jslib` | The browser's flow (only compiled into Web builds). |
| `ITokenClient` / `HttpTokenClient` | POSTs to `/oauth2/token` (code exchange, refresh) and `/oauth2/revoke`, with `UnityWebRequest`. |
| `JwtClaims` | Reads the player's id and name out of the access token (a JWT). |
| `TokenStore` | The refresh token and profile under `Account.*`: localStorage in a browser, PlayerPrefs elsewhere. |
| `AccountManager` | The SignedOut / SigningIn / SignedIn state machine, refreshing, sign-out. Raises `Changed`. |
| `AccountMenu` | The Account button and window. Built by **Match3 > Build Account Menu** (`Assets/Editor/AccountMenuBuilder.cs`). |

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

### Staying signed in

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

- **Web build:** yes.
- **Windows build and the Editor:** not yet. The Account window says sign-in is only in the
  browser version. (The Editor can't run a `.jslib`: `WebRedirectAuthFlow` is compiled only
  when `UNITY_WEBGL && !UNITY_EDITOR`.) Windows sign-in, with a loopback redirect to
  `127.0.0.1`, is a separate issue.
- Like Settings, the Account button is only shown between rounds.

## Testing against a local accounts server

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
covers the state machine with a fake `IAuthFlow` and a fake token server.
