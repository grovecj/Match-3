using System;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Accounts
{
    /// <summary>
    /// PKCE ("pixie", Proof Key for Code Exchange, RFC 7636): how a game signs in with OAuth
    /// without a client secret. A secret baked into a game isn't secret (anyone can dig it out
    /// of the build), so instead each sign-in makes up a fresh one-time secret:
    ///
    ///   1. Before sign-in, make a random <b>code verifier</b> and keep it to ourselves.
    ///   2. Send only its SHA-256 hash, the <b>code challenge</b>, with the sign-in request.
    ///   3. When the auth server sends back an authorization code, trade the code for tokens
    ///      and send the verifier with it. The server hashes the verifier and checks it matches
    ///      the challenge it saw in step 2.
    ///
    /// Someone who steals the code on its way back (a malicious browser extension, say) can't
    /// use it: they'd need the verifier, which never left the game.
    ///
    /// The same random-string helper also makes the <c>state</c> value (see
    /// <see cref="AuthCallback"/>).
    /// </summary>
    public static class Pkce
    {
        /// <summary>The only challenge method we use: the challenge is SHA-256 of the verifier.</summary>
        public const string ChallengeMethod = "S256";

        /// <summary>
        /// Makes a new code verifier: 32 random bytes as base64url, which is 43 characters
        /// (RFC 7636 allows 43-128).
        /// </summary>
        public static string CreateVerifier() => RandomString(32);

        /// <summary>The S256 challenge for <paramref name="verifier"/>: base64url(SHA-256(verifier)).</summary>
        public static string CreateChallenge(string verifier)
        {
            // The verifier only ever holds A-Z a-z 0-9 - . _ ~, so ASCII bytes are its exact bytes.
            byte[] bytes = Encoding.ASCII.GetBytes(verifier);
            using (SHA256 sha256 = SHA256.Create())
            {
                return Base64Url.Encode(sha256.ComputeHash(bytes));
            }
        }

        /// <summary>
        /// <paramref name="byteCount"/> bytes from a cryptographically secure random number
        /// generator, as base64url. System.Random would be the wrong tool: it's predictable, and
        /// the whole point of the verifier and state is that nobody else can guess them.
        /// </summary>
        public static string RandomString(int byteCount)
        {
            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base64Url.Encode(bytes);
        }
    }

    /// <summary>
    /// base64url (RFC 4648 §5): base64 made safe for URLs by using '-' and '_' instead of '+'
    /// and '/', and leaving off the '=' padding. OAuth and JWTs use it everywhere.
    /// </summary>
    public static class Base64Url
    {
        public static string Encode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static byte[] Decode(string text)
        {
            string base64 = text.Replace('-', '+').Replace('_', '/');
            // Put the padding back: base64 text always comes in groups of 4 characters.
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}
