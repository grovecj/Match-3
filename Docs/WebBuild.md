# Web build

Notes from getting Match-3 running in the browser (issue #22). Automatic deploys are covered
by the CI/CD issues; this page covers making the build and what to watch out for.

## Making a build

1. Install the **Web** module: Unity Hub → Installs → 6000.5.7f1 → ⚙ → Add modules → *Web Build Support*.
2. In the Editor, use **Match3 → Build Web** (or **Build Web and Run** to open it in your browser).
   The first build switches the project to the Web platform and re-imports textures, so it's slow.
3. Output goes to `Builds/Web/` (git-ignored). Serve that folder with any static web server, e.g.

   ```bash
   python -m http.server 8000 -d Builds/Web
   ```

   then open <http://localhost:8000>. Opening `index.html` straight from disk (`file://`) doesn't
   work: browsers block the loader from fetching the build files.

The build script is [`Assets/Editor/WebBuild.cs`](../Assets/Editor/WebBuild.cs). It sets the Player
Settings below in code, so every build uses the same settings. CI can run it headless:

```bash
Unity.exe -batchmode -quit -projectPath . -buildTarget WebGL -executeMethod WebBuild.BuildFromCommandLine
```

## Settings chosen

| Setting | Value | Why |
|---|---|---|
| Scripting backend | IL2CPP (the only option) | Browsers can't run Mono. C# is compiled to C++ and then to WebAssembly with Emscripten. That C++ step is most of the build time. |
| Compression | **Gzip** | Brotli is ~10–20% smaller, but browsers only accept it over HTTPS and it builds more slowly. Gzip works on plain `http://` too, and its fallback decompression is faster. |
| Decompression Fallback | **On** | The `.gz` files need the server to send `Content-Encoding: gzip`. GitHub Pages, itch.io and `python -m http.server` don't. With the fallback, the loader unzips the files in JavaScript instead of failing to load. |
| Template | **PROJECT:FullWindow** | Our own template in `Assets/WebGLTemplates/FullWindow`. The canvas fills the window and follows resizes, keyboard focus goes to the game on load, and a simple progress bar shows while it loads. Unity's built-in templates use a fixed 960×600 canvas on desktop. |
| Data Caching | On | Keeps the downloaded build in the browser's IndexedDB, so reloads start faster. |
| Run In Background | **On** | With it off, Unity stops rendering whenever the page loses focus, so resizing the window while unfocused leaves a blank canvas. We don't need Unity to freeze the game: `GameManager` pauses the round on focus loss, and browsers throttle hidden tabs anyway. |
| `autoSyncPersistentDataPath` | `true` (in the template) | Files written to `Application.persistentDataPath` (the leaderboard) are copied to IndexedDB as soon as they change. Unity 6 logs a deprecation warning without it. |

## Measurements

Unity 6000.5.7f1, Windows 11. The build numbers come from the Console line that **Match3 → Build Web** logs.

| | |
|---|---|
| Build time, first build | ~8 min for the IL2CPP/Emscripten compile (476 of 506 build steps), plus the platform switch |
| Build time, later builds | 3:58–6:30 total, as logged by Match3 → Build Web (the IL2CPP cache makes these faster) |
| Download size (`Build/` folder, gzipped) | **19.0 MB** with the leaderboard: `.wasm` 12.0 MB, `.data` 6.9 MB, framework 83 KB, loader 47 KB |
| Load time, Chromium, localhost (build cache cleared / cached) | ~1.8 s / ~1.3 s to playable |
| Load time, Firefox | _TBD_ |

Load times over localhost are mostly decompression and startup. Over a real network, the
19 MB download will dominate the first visit.

## Browser gotchas

- **Esc belongs to the browser.** In fullscreen, Esc exits fullscreen before the game ever
  sees the key. The Pause action is now bound to **P** as well as Esc (and gamepad Start).
- **Focus loss auto-pauses.** `GameManager` pauses in `OnApplicationFocus(false)` and
  `OnApplicationPause(true)`. On Web these fire when you click the address bar, switch tabs,
  or switch apps.
- **Keyboard input needs focus.** The canvas only gets key presses while it has focus. The
  template focuses it after loading. If keys stop working, click the game once.
- **PlayerPrefs live in IndexedDB.** On Web, PlayerPrefs and `Application.persistentDataPath`
  are stored in the browser's IndexedDB for that site (origin). That means:
  - Data is per origin: `localhost:8000` and `localhost:8001` have separate saves, and so do
    each deploy URL.
  - Call `PlayerPrefs.Save()` after changing a value (ThemeManager does). Otherwise the change
    may not be written before the tab closes.
  - Private/incognito windows throw the data away when they close.
- **Pause blur.** `BoardBlur` renders the camera into a RenderTexture and downsamples it.
  This needs render-to-texture support, which WebGL2 has. If the render pipeline refuses the
  request, `BoardBlur` hides the board behind a black image instead.
- **Template preprocessor gotchas.** Unity runs its own preprocessor over the template before
  the browser sees it. Any line starting with `#` is read as a directive, including CSS id
  selectors (`#unity-canvas {`) and lines inside HTML comments. Triple-brace tags are
  evaluated as JavaScript, even in comments. Both fail the build with a
  "Preprocessor error" at the very end. The template writes `canvas#unity-canvas` for this
  reason.
- **Desktop only.** Touch input is out of scope. Unity doesn't officially support mobile
  browsers for this project's setup.

## Test results

Tested in Chromium (the Claude desktop app's browser pane) against `python -m http.server`. That
server sends no `Content-Encoding` header, so Decompression Fallback was exercised too.

| Check | Chromium | Firefox |
|---|---|---|
| Loads (WebGL 2.0 context) | ✅ | _TBD_ |
| Swap / match / cascade, score | ✅ | _TBD_ |
| Timer counts down, round ends with final score + Play Again | ✅ | _TBD_ |
| P pauses and resumes; Esc too (outside fullscreen) | ✅ | _TBD_ |
| Losing focus (switching tabs) auto-pauses | ✅ | _TBD_ |
| Pause blur renders (no black-image fallback needed) | ✅ | _TBD_ |
| Canvas fills the window and follows resizes | ✅ | _TBD_ |
| Settings (token style, board colors) survive a reload | ✅ | _TBD_ |
| Leaderboard survives a reload (`leaderboard.json` in IndexedDB) | ✅ | _TBD_ |
| No console errors | ✅ | _TBD_ |

## Known issues

- **Narrow windows.** In a tall, narrow window (portrait), the side panel covers most of
  the board. Fine at common desktop sizes, but the layout doesn't adapt to portrait.
- **Download size.** The 12 MB `.wasm` is most of the download. Worth trying later:
  Managed Stripping Level *High* and IL2CPP Code Generation *Optimize for size*. Both
  need a full retest, because stripping can remove code that's only reached via reflection.
