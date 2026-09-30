using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using UnityEngine;
#endif

namespace Match3.Accounts
{
    /// <summary>
    /// Remembers who's signed in between launches: the refresh token and a little profile
    /// (id and name), under "Account.*" keys. The access token is deliberately <b>not</b>
    /// saved: it only lives 15 minutes, and a fresh one is a refresh away.
    ///
    /// Where it's kept depends on the platform:
    ///   - In a browser, the page's localStorage (through Assets/Plugins/WebGL/Auth.jslib).
    ///     Not PlayerPrefs: there, PlayerPrefs.Save() copies to IndexedDB <i>asynchronously</i>,
    ///     so a reload right after a refresh could lose the rotated refresh token and bring
    ///     back the old one. The server treats reuse of an old one as theft and revokes the
    ///     whole chain, signing the player out. localStorage is written immediately.
    ///   - Everywhere else, PlayerPrefs (the registry on Windows), which Save() writes at once.
    ///
    /// How safe is this? Both are private to this user and this site, but not encrypted.
    /// That's the usual trade-off for a game (and for most single-page web apps). What limits
    /// the damage if a refresh token does leak is the server: tokens are rotated on every use,
    /// a reused old one revokes the whole chain, and Sign out revokes it outright.
    /// </summary>
    public sealed class TokenStore
    {
        /// <summary>The key prefix the game uses. Tests use their own, so they don't touch it.</summary>
        public const string DefaultPrefix = "Account.";

        private readonly string refreshTokenKey;
        private readonly string userIdKey;
        private readonly string nameKey;
        private readonly string refreshLeaseKey;

        /// <summary>
        /// How long a refresh lease lasts if its tab never releases it (closed mid-refresh).
        /// Longer than a token request's timeout, so a slow refresh keeps its lease.
        /// </summary>
        public static readonly TimeSpan RefreshLeaseDuration = TimeSpan.FromSeconds(30);

        public TokenStore(string prefix = DefaultPrefix)
        {
            refreshTokenKey = prefix + "RefreshToken";
            userIdKey = prefix + "UserId";
            nameKey = prefix + "Name";
            refreshLeaseKey = prefix + "RefreshLease";
        }

        public string RefreshToken => Get(refreshTokenKey);
        public string UserId => Get(userIdKey);
        public string Name => Get(nameKey);

        public bool HasRefreshToken => RefreshToken.Length > 0;

        /// <summary>Saves a (new) refresh token, plus the profile if it's known.</summary>
        public void Save(string refreshToken, JwtClaims profile)
        {
            Set(refreshTokenKey, refreshToken ?? "");
            if (profile != null)
            {
                Set(userIdKey, profile.sub ?? "");
                Set(nameKey, profile.name ?? "");
            }
            Flush();
        }

        public void Clear()
        {
            Delete(refreshTokenKey);
            Delete(userIdKey);
            Delete(nameKey);
            Delete(refreshLeaseKey);
            Flush();
        }

        // ---- The refresh lease ------------------------------------------------------------
        // Every tab of the game in a browser shares this store. If two of them refreshed at
        // the same moment, both would send the same refresh token, and the server would treat
        // the second use as theft and revoke the account's whole token chain. So a tab first
        // takes the "refresh lease": it writes "<its id>|<expiry>" here, and only refreshes
        // while that's still what's stored. Other tabs wait until it's released or expires.

        /// <summary>
        /// Takes the lease for <paramref name="owner"/>, unless someone else holds an unexpired
        /// one. True if written. Check <see cref="HoldsRefreshLease"/> a moment later: if two
        /// tabs write at the same time, only the last write counts.
        /// </summary>
        public bool TryTakeRefreshLease(string owner)
        {
            if (TryReadLease(out string holder) && holder != owner)
            {
                return false;
            }
            long expires = DateTimeOffset.UtcNow.Add(RefreshLeaseDuration).ToUnixTimeMilliseconds();
            Set(refreshLeaseKey, owner + "|" + expires);
            Flush();
            return true;
        }

        public bool HoldsRefreshLease(string owner) => TryReadLease(out string holder) && holder == owner;

        /// <summary>Releases the lease if <paramref name="owner"/> holds it.</summary>
        public void ReleaseRefreshLease(string owner)
        {
            if (HoldsRefreshLease(owner))
            {
                Delete(refreshLeaseKey);
                Flush();
            }
        }

        // True with the holder's id if there's an unexpired lease.
        private bool TryReadLease(out string holder)
        {
            holder = null;
            string[] parts = Get(refreshLeaseKey).Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[1], out long expires) ||
                expires <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                return false;
            }
            holder = parts[0];
            return true;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Implemented in Assets/Plugins/WebGL/Auth.jslib (see WebRedirectAuthFlow for how
        // [DllImport("__Internal")] works).
        [DllImport("__Internal")] private static extern void Match3Auth_LocalSet(string key, string value);
        [DllImport("__Internal")] private static extern string Match3Auth_LocalGet(string key);
        [DllImport("__Internal")] private static extern void Match3Auth_LocalRemove(string key);

        private static string Get(string key) => Match3Auth_LocalGet(key) ?? "";
        private static void Set(string key, string value) => Match3Auth_LocalSet(key, value);
        private static void Delete(string key) => Match3Auth_LocalRemove(key);
        private static void Flush() { } // localStorage writes are already done.
#else
        private static string Get(string key) => PlayerPrefs.GetString(key, "");
        private static void Set(string key, string value) => PlayerPrefs.SetString(key, value);
        private static void Delete(string key) => PlayerPrefs.DeleteKey(key);

        // PlayerPrefs are only written to disk on Save() (or when the game quits). Saving now
        // means a crash can't lose a rotated refresh token.
        private static void Flush() => PlayerPrefs.Save();
#endif
    }
}
