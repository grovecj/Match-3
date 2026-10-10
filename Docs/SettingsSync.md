# Settings sync

A signed-in player's settings (token style, board theme, sound volume) follow their account,
so choosing a theme in the web build and then signing in on Windows shows the same theme
there. Signed out, nothing changes: settings live in PlayerPrefs, as before.

The copy on the account is kept by the leaderboard server
([grovecj/match-3-api](https://github.com/grovecj/match-3-api)), at `/me/preferences`.

## The rules

PlayerPrefs (`Settings.TokenStyle`, `Settings.BoardTheme`, `Settings.SoundVolume`) is still
where the game reads its settings from. `PreferencesSync` only copies between PlayerPrefs and
the server:

| When | What happens |
| --- | --- |
| You sign in, or the game starts already signed in | The account's settings are loaded and applied here. **The server wins.** |
| The Settings menu closes with something changed | This device's settings are saved to the account. **The last save wins**, even over another device's. |
| A save can't be sent (offline, server down) | The change still applies here, and is marked *pending* in PlayerPrefs. It's sent the next time Settings closes or the game starts. |
| The game starts with a save pending | This device's settings are the newer ones, so they go up instead of the server's coming down. |
| The account has nothing saved yet | This device's settings become the account's first. |
| A different account signs in while a save is pending | The pending save is dropped (the settings it was for have just been replaced), so one player's settings never reach another's account. |

The sound set (`Settings.SoundSet`) isn't synced: it's saved in PlayerPrefs only, so each
device keeps its own.

Two devices that are both running don't see each other's changes live: a device picks up the
account's settings when it signs in or starts, not while it's open.

## What's sent

```json
{ "data": { "tokenStyle": "gems", "boardTheme": "ocean", "soundVolume": 0.8 }, "version": 3 }
```

Styles and themes are named by their asset's `Id`, the same strings PlayerPrefs holds. A
setting that's missing, or an id this build doesn't have (saved by a newer version of the
game, say), is skipped, and that setting stays as it is on this device.

### Versions

`version` is the server's count of saves. A save says which version it's replacing, and the
server only accepts it if that's still the stored one, then answers with the next. This is
*optimistic concurrency*: nobody locks anything; a save that was based on out-of-date
settings is simply turned down (`409 version_conflict`).

```
Windows (at version 3)                 server                    Web (at version 3)
                                         | <---- PUT version 3 ----- theme = ocean
                                         | ----- now version 4 ---->
volume = 40% --- PUT version 3 --------> |
             <-- 409 version_conflict -- |      "3 isn't the stored version any more"
             --- GET ------------------> |
             <-- version 4 ------------- |
             --- PUT version 4 --------> |      Windows' settings, whole, replace Web's
             <-- now version 5 --------- |
```

What to do about a conflict is the game's choice. Ours is the simplest: last write wins, so
`PreferencesSync` asks for the current version and saves over it. (Merging setting by setting
would keep Web's theme *and* Windows' volume above; with three settings it isn't worth it.)

## The pieces

In `Assets/Scripts/Online/` (namespace `Match3.Online`):

| Class | Does |
| --- | --- |
| `IPreferencesApi` | The two requests (`GET` and `PUT /me/preferences`) as coroutines, plus `SavedPreferences` / `PreferenceValues`. |
| `HttpPreferencesApi` | The real one. Shares `HttpLeaderboardApi`'s request code (bearer token, one retry on a dropped connection). |
| `PreferencesJson` | Reads and writes the JSON with `JsonUtility`, and works out whether a volume was sent at all (a missing number would otherwise read as 0: muted). |
| `PreferencesSync` | The MonoBehaviour that applies the rules above. Listens to `AccountManager.Changed` and `SettingsMenu.Closed`; applies settings through `ThemeManager` and `AudioManager`. |

`PreferencesSync` sits on the `Settings` object next to the `SettingsMenu`. **Match3 > Build
Settings Menu** puts it there and wires it (it needs the scene's `AccountManager`, so build
the Account menu first).

Nothing here shows the player an error. If syncing fails, the settings still work on this
device, and the Console has a "Settings sync: ..." warning saying why.

## Tests

`PreferencesJsonTests` (Edit Mode) reads replies in the server's shapes, with settings missing
and unknown. `PreferencesSyncPlayModeTests` (Play Mode) runs the sync with a real
`ThemeManager` and `AudioManager` against a fake server that keeps versions like the real
one: signing in, saving a change, being offline and catching up, and a conflict.

As with the global leaderboard, a web build served from `localhost` can't call the real server
(a different origin), so try this in the Editor, the Windows build, or on the real site.
