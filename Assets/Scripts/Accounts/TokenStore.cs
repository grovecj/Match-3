using UnityEngine;

namespace Match3.Accounts
{
    /// <summary>
    /// Remembers who's signed in between launches: the refresh token and a little profile
    /// (id and name), in PlayerPrefs under "Account.*" keys. The access token is deliberately
    /// <b>not</b> saved: it only lives 15 minutes, and a fresh one is a refresh away.
    ///
    /// How safe is this? PlayerPrefs is the registry on Windows and the page's IndexedDB in a
    /// browser: private to this user and this site, but not encrypted. That's the usual
    /// trade-off for a game (and for most single-page web apps). What limits the damage if a
    /// refresh token does leak is the server: tokens are rotated on every use, a reused old
    /// one revokes the whole chain, and Sign out revokes it outright.
    /// </summary>
    public sealed class TokenStore
    {
        /// <summary>The key prefix the game uses. Tests use their own, so they don't touch it.</summary>
        public const string DefaultPrefix = "Account.";

        private readonly string refreshTokenKey;
        private readonly string userIdKey;
        private readonly string nameKey;

        public TokenStore(string prefix = DefaultPrefix)
        {
            refreshTokenKey = prefix + "RefreshToken";
            userIdKey = prefix + "UserId";
            nameKey = prefix + "Name";
        }

        public string RefreshToken => PlayerPrefs.GetString(refreshTokenKey, "");
        public string UserId => PlayerPrefs.GetString(userIdKey, "");
        public string Name => PlayerPrefs.GetString(nameKey, "");

        public bool HasRefreshToken => RefreshToken.Length > 0;

        /// <summary>Saves a (new) refresh token, plus the profile if it's known.</summary>
        public void Save(string refreshToken, JwtClaims profile)
        {
            PlayerPrefs.SetString(refreshTokenKey, refreshToken ?? "");
            if (profile != null)
            {
                PlayerPrefs.SetString(userIdKey, profile.sub ?? "");
                PlayerPrefs.SetString(nameKey, profile.name ?? "");
            }
            // PlayerPrefs are only written to disk on Save() (or when the game quits). Saving
            // now means a crash or closed tab can't lose a rotated refresh token.
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(refreshTokenKey);
            PlayerPrefs.DeleteKey(userIdKey);
            PlayerPrefs.DeleteKey(nameKey);
            PlayerPrefs.Save();
        }
    }
}
