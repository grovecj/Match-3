using System;
using System.Text;
using UnityEngine;

namespace Match3.Accounts
{
    /// <summary>
    /// The claims (facts about the player) inside an access token.
    ///
    /// Our access tokens are JWTs (JSON Web Tokens): three base64url parts joined by dots,
    /// <c>header.payload.signature</c>. The payload is plain JSON anyone can read; the
    /// signature is what makes it trustworthy to our <i>servers</i>, which check it against the
    /// accounts server's public keys. The game doesn't check it: it got the token straight from
    /// the accounts server over HTTPS, and only uses the name for display. Anything that
    /// matters (like accepting a score) is decided by a server that does check.
    /// </summary>
    [Serializable]
    public class JwtClaims
    {
        // Lowercase to match the JSON keys, for JsonUtility.

        /// <summary>"Subject": the player's permanent id (a UUID).</summary>
        public string sub;
        /// <summary>The player's display name, from their Google account.</summary>
        public string name;
        /// <summary>A URL of the player's picture.</summary>
        public string picture;
        /// <summary>When the token expires, in seconds since 1970 (Unix time).</summary>
        public long exp;

        /// <summary>Reads the claims from a JWT, or returns null if it isn't one.</summary>
        public static JwtClaims Read(string jwt)
        {
            if (string.IsNullOrEmpty(jwt))
            {
                return null;
            }
            string[] parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }
            try
            {
                string json = Encoding.UTF8.GetString(Base64Url.Decode(parts[1]));
                return JsonUtility.FromJson<JwtClaims>(json);
            }
            catch (FormatException)
            {
                return null; // Not valid base64.
            }
            catch (ArgumentException)
            {
                return null; // Not valid JSON.
            }
        }
    }
}
