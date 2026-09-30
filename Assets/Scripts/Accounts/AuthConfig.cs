using System;

namespace Match3.Accounts
{
    /// <summary>
    /// Where the cartergrove.me accounts server lives, and how this game is registered with it.
    ///
    /// The server (grovecj/accounts) is an OAuth 2.1 / OpenID Connect provider. Each app that
    /// signs players in is registered there as a <b>client</b> with an id and the exact
    /// redirect URIs it may use. Our clients are "public": they have no secret (see
    /// <see cref="Pkce"/> for why), which is why nothing here is sensitive.
    /// </summary>
    public static class AuthConfig
    {
        /// <summary>The accounts server. AccountManager lets you point it somewhere else for testing.</summary>
        public const string DefaultIssuer = "https://auth.cartergrove.me";

        /// <summary>The client id for the browser build. Its redirect URI is the game's page.</summary>
        public const string WebClientId = "match3-web";

        /// <summary>The client id for the Windows build (a later issue). It redirects to 127.0.0.1.</summary>
        public const string DesktopClientId = "match3-desktop";

        /// <summary>
        /// What we ask for. "openid" and "profile" (OpenID Connect) get the player's id and
        /// name into the tokens.
        /// </summary>
        public const string Scope = "openid profile";

        public static string AuthorizeEndpoint(string issuer) => issuer.TrimEnd('/') + "/oauth2/authorize";
        public static string TokenEndpoint(string issuer) => issuer.TrimEnd('/') + "/oauth2/token";
        public static string RevokeEndpoint(string issuer) => issuer.TrimEnd('/') + "/oauth2/revoke";

        /// <summary>
        /// The URL that starts sign-in: the accounts server's authorize endpoint, with the
        /// details of this request in the query string. The player's browser goes there, signs
        /// in with Google, and is sent back to <paramref name="redirectUri"/> with a code.
        /// </summary>
        public static string AuthorizeUrl(string issuer, string clientId, string redirectUri,
            string codeChallenge, string state)
        {
            // Uri.EscapeDataString percent-encodes anything that isn't safe in a query value
            // (for example the ':' and '/' in the redirect URI, and the space in the scope).
            return AuthorizeEndpoint(issuer) +
                "?response_type=code" +
                "&client_id=" + Uri.EscapeDataString(clientId) +
                "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                "&scope=" + Uri.EscapeDataString(Scope) +
                "&state=" + Uri.EscapeDataString(state) +
                "&code_challenge=" + Uri.EscapeDataString(codeChallenge) +
                "&code_challenge_method=" + Pkce.ChallengeMethod;
        }
    }
}
