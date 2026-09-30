using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Match3.Accounts;
using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for <see cref="Pkce"/>, <see cref="Base64Url"/> and
    /// <see cref="AuthConfig.AuthorizeUrl"/>.
    /// </summary>
    public class PkceTests
    {
        // base64url's alphabet, and nothing else (no '+', '/' or '=' padding).
        private static readonly Regex Base64UrlOnly = new Regex("^[A-Za-z0-9_-]+$");

        [Test]
        public void CreateChallenge_MatchesTheRfc7636TestVector()
        {
            // RFC 7636, Appendix B: the worked example every PKCE implementation checks against.
            const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
            const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

            Assert.That(Pkce.CreateChallenge(verifier), Is.EqualTo(expectedChallenge));
        }

        [Test]
        public void CreateVerifier_Is43Base64UrlCharacters()
        {
            string verifier = Pkce.CreateVerifier();

            // 32 random bytes = 43 base64url characters, the RFC's minimum length.
            Assert.That(verifier.Length, Is.EqualTo(43));
            Assert.That(Base64UrlOnly.IsMatch(verifier), Is.True, verifier);
        }

        [Test]
        public void CreateVerifier_IsDifferentEachTime()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                Assert.That(seen.Add(Pkce.CreateVerifier()), Is.True, "A verifier repeated.");
            }
        }

        [Test]
        public void Base64Url_RoundTripsEveryPaddingLength()
        {
            // 1, 2 and 3 bytes need 2, 1 and 0 '=' in plain base64; Decode has to put them back.
            foreach (string text in new[] { "a", "ab", "abc", "abcd", "ÿþ?>" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                string encoded = Base64Url.Encode(bytes);

                Assert.That(Base64UrlOnly.IsMatch(encoded), Is.True, encoded);
                Assert.That(Base64Url.Decode(encoded), Is.EqualTo(bytes));
            }
        }

        [Test]
        public void AuthorizeUrl_HasEveryParameterEncoded()
        {
            string url = AuthConfig.AuthorizeUrl("https://auth.example.com/", "match3-web",
                "https://games.example.com/match3/", "CHALLENGE", "STATE");

            Assert.That(url, Does.StartWith("https://auth.example.com/oauth2/authorize?"));
            Dictionary<string, string> query = AuthCallback.ParseQuery(url);
            Assert.That(query["response_type"], Is.EqualTo("code"));
            Assert.That(query["client_id"], Is.EqualTo("match3-web"));
            Assert.That(query["redirect_uri"], Is.EqualTo("https://games.example.com/match3/"));
            Assert.That(query["scope"], Is.EqualTo(AuthConfig.Scope));
            Assert.That(query["state"], Is.EqualTo("STATE"));
            Assert.That(query["code_challenge"], Is.EqualTo("CHALLENGE"));
            Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
            // The redirect URI is percent-encoded, not pasted in raw.
            Assert.That(url, Does.Contain("redirect_uri=https%3A%2F%2Fgames.example.com%2Fmatch3%2F"));
        }
    }
}
