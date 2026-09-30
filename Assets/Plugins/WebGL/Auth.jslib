// Browser-side helpers for WebRedirectAuthFlow (Assets/Scripts/Accounts/WebRedirectAuthFlow.cs).
//
// A .jslib is JavaScript that Unity links into the browser build. Each function in the object
// passed to mergeInto becomes callable from C# through [DllImport("__Internal")], by the same
// name. Strings cross the boundary as pointers into the game's memory: UTF8ToString reads one
// coming in, and to return one we copy it into memory allocated with _malloc (C# frees it).
//
// The file lives in Assets/Plugins/WebGL so its import settings are "WebGL only".

mergeInto(LibraryManager.library, {

  // Sends this tab to the URL (the accounts server's sign-in page). Unlike Application.OpenURL,
  // which opens a new tab.
  Match3Auth_Navigate: function (url) {
    window.location.assign(UTF8ToString(url));
  },

  // Changes the address bar without reloading, and without adding a Back-button entry.
  Match3Auth_ReplaceUrl: function (url) {
    window.history.replaceState(window.history.state, "", UTF8ToString(url));
  },

  // sessionStorage: per-tab key/value storage that's written immediately and cleared when the
  // tab is closed. Wrapped in try/catch because browsers can block storage (e.g. some private
  // modes), and a sign-in that can't save its state should fail quietly, not crash the game.
  Match3Auth_SessionSet: function (key, value) {
    try {
      window.sessionStorage.setItem(UTF8ToString(key), UTF8ToString(value));
    } catch (e) {
      console.warn("Match3Auth: sessionStorage unavailable", e);
    }
  },

  Match3Auth_SessionGet: function (key) {
    var value = null;
    try {
      value = window.sessionStorage.getItem(UTF8ToString(key));
    } catch (e) {
      console.warn("Match3Auth: sessionStorage unavailable", e);
    }
    if (value === null) {
      return 0; // A null pointer, which C# sees as a null string.
    }
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  Match3Auth_SessionRemove: function (key) {
    try {
      window.sessionStorage.removeItem(UTF8ToString(key));
    } catch (e) {
      console.warn("Match3Auth: sessionStorage unavailable", e);
    }
  },

  // localStorage: like sessionStorage, but kept after the tab closes. TokenStore keeps the
  // signed-in account here (see TokenStore.cs for why not PlayerPrefs).
  Match3Auth_LocalSet: function (key, value) {
    try {
      window.localStorage.setItem(UTF8ToString(key), UTF8ToString(value));
    } catch (e) {
      console.warn("Match3Auth: localStorage unavailable", e);
    }
  },

  Match3Auth_LocalGet: function (key) {
    var value = null;
    try {
      value = window.localStorage.getItem(UTF8ToString(key));
    } catch (e) {
      console.warn("Match3Auth: localStorage unavailable", e);
    }
    if (value === null) {
      return 0;
    }
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  Match3Auth_LocalRemove: function (key) {
    try {
      window.localStorage.removeItem(UTF8ToString(key));
    } catch (e) {
      console.warn("Match3Auth: localStorage unavailable", e);
    }
  },
});
