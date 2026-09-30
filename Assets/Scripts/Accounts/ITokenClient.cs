using System;
using System.Collections;

namespace Match3.Accounts
{
    /// <summary>
    /// Talks to the accounts server's token endpoints: trading an authorization code for
    /// tokens, refreshing them, and revoking them on sign-out. <see cref="HttpTokenClient"/>
    /// is the real one; tests plug in a fake so they never touch the network.
    ///
    /// Each method is a coroutine (run it with StartCoroutine) that calls <c>done</c> once the
    /// server has answered.
    /// </summary>
    public interface ITokenClient
    {
        IEnumerator ExchangeCode(AuthFlowResult code, Action<TokenResult> done);
        IEnumerator Refresh(string refreshToken, Action<TokenResult> done);
        /// <summary>Asks the server to forget a refresh token. Best effort: failures are ignored.</summary>
        IEnumerator Revoke(string refreshToken);
    }

    /// <summary>The outcome of a token request.</summary>
    public sealed class TokenResult
    {
        public bool Succeeded { get; private set; }

        /// <summary>
        /// A short-lived token (15 minutes) that proves who the player is to our servers. It's a
        /// JWT: see <see cref="JwtClaims"/>.
        /// </summary>
        public string AccessToken { get; private set; }

        /// <summary>
        /// A long-lived token (30 days) used only to get new access tokens. The server
        /// <b>rotates</b> it: every refresh returns a new one and retires the old one, so the
        /// new one must be saved each time.
        /// </summary>
        public string RefreshToken { get; private set; }

        public int ExpiresInSeconds { get; private set; }

        /// <summary>
        /// True only if the server said "invalid_grant": the code or refresh token has expired,
        /// been revoked or already been used, so trying again won't help. False for network
        /// problems, server errors and other OAuth errors, which may go away.
        /// </summary>
        public bool Rejected { get; private set; }

        /// <summary>What went wrong, for the log. Null on success.</summary>
        public string Error { get; private set; }

        public static TokenResult Success(string accessToken, string refreshToken, int expiresInSeconds)
        {
            return new TokenResult
            {
                Succeeded = true,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresInSeconds = expiresInSeconds,
            };
        }

        public static TokenResult Failure(string error, bool rejected)
        {
            return new TokenResult { Error = error, Rejected = rejected };
        }
    }
}
