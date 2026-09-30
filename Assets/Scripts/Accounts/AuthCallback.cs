using System;
using System.Collections.Generic;
using System.Text;

namespace Match3.Accounts
{
    /// <summary>
    /// The query string the accounts server sends the player back with after sign-in, e.g.
    /// <c>https://games.cartergrove.me/match3/?code=abc&amp;state=xyz</c>, or
    /// <c>?error=access_denied&amp;state=xyz</c> if they cancelled.
    ///
    /// <b>Why check state?</b> Anyone can make a link to our page with a code on it. Without a
    /// check, an attacker could sign in with their own account, stop before their code is used,
    /// and trick a player into opening <c>...match3/?code=THEIRS</c>: the player would be
    /// playing (and posting scores) as the attacker. So before sign-in we make up a random
    /// <c>state</c>, remember it, and the server hands it back unchanged. A callback whose
    /// state isn't the one we remember didn't come from a sign-in we started, so we drop it.
    ///
    /// Plain C# (no Unity), so it's covered by Edit Mode tests.
    /// </summary>
    public sealed class AuthCallback
    {
        // The query parameters that belong to the sign-in callback. "iss" is the issuer, which
        // some servers add (RFC 9207). These are removed from the address bar afterwards.
        private static readonly HashSet<string> CallbackParameters = new HashSet<string>
        {
            "code", "state", "error", "error_description", "error_uri", "iss",
        };

        public string Code { get; private set; }
        public string State { get; private set; }
        /// <summary>An OAuth error code such as "access_denied", or null.</summary>
        public string Error { get; private set; }
        public string ErrorDescription { get; private set; }

        /// <summary>
        /// Reads the callback from <paramref name="url"/>. Returns false if the URL isn't a
        /// sign-in callback at all (it has neither a code nor an error), which is the normal
        /// case: the player just opened the game.
        /// </summary>
        public static bool TryParse(string url, out AuthCallback callback)
        {
            Dictionary<string, string> query = ParseQuery(url);
            query.TryGetValue("code", out string code);
            query.TryGetValue("error", out string error);
            if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(error))
            {
                callback = null;
                return false;
            }

            query.TryGetValue("state", out string state);
            query.TryGetValue("error_description", out string errorDescription);
            callback = new AuthCallback
            {
                Code = code,
                State = state,
                Error = error,
                ErrorDescription = errorDescription,
            };
            return true;
        }

        /// <summary>
        /// Checks the callback against the state we saved before sign-in. Returns null if it's
        /// good (there's a code, and the state matches), or a message for the player if not.
        /// </summary>
        public string Validate(string expectedState)
        {
            // No saved state means we never started a sign-in (or it was from another tab or an
            // old visit), and a missing or different state means it isn't the one we started.
            // Either way, don't trust anything else in the callback, including an error.
            if (string.IsNullOrEmpty(expectedState) || string.IsNullOrEmpty(State) ||
                !string.Equals(State, expectedState, StringComparison.Ordinal))
            {
                return "Sign-in failed: the response didn't match the request. Please try again.";
            }
            if (Error == "access_denied")
            {
                return "Sign-in was cancelled.";
            }
            if (!string.IsNullOrEmpty(Error))
            {
                return "Sign-in failed (" + Error + "). Please try again.";
            }
            if (string.IsNullOrEmpty(Code))
            {
                return "Sign-in failed: no authorization code. Please try again.";
            }
            return null;
        }

        /// <summary>
        /// <paramref name="url"/> without the callback's parameters (code, state, ...), keeping
        /// any others and the #fragment. Used to tidy the address bar, so a reload or a
        /// bookmark doesn't try to use the (single-use) code again.
        /// </summary>
        public static string RemoveCallbackParameters(string url)
        {
            SplitUrl(url, out string beforeQuery, out string query, out string fragment);
            var kept = new List<string>();
            if (query.Length > 0)
            {
                foreach (string pair in query.Split('&'))
                {
                    string name = Decode(pair.Split('=')[0]);
                    if (pair.Length > 0 && !CallbackParameters.Contains(name))
                    {
                        kept.Add(pair);
                    }
                }
            }
            var result = new StringBuilder(beforeQuery);
            if (kept.Count > 0)
            {
                result.Append('?').Append(string.Join("&", kept));
            }
            return result.Append(fragment).ToString();
        }

        /// <summary><paramref name="url"/> without any query string or fragment.</summary>
        public static string WithoutQuery(string url)
        {
            SplitUrl(url, out string beforeQuery, out _, out _);
            return beforeQuery;
        }

        /// <summary>The query string's name=value pairs, percent-decoded. Later duplicates win.</summary>
        public static Dictionary<string, string> ParseQuery(string url)
        {
            SplitUrl(url, out _, out string query, out _);
            var values = new Dictionary<string, string>();
            if (query.Length == 0)
            {
                return values;
            }
            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }
                int equals = pair.IndexOf('=');
                string name = equals < 0 ? pair : pair.Substring(0, equals);
                string value = equals < 0 ? "" : pair.Substring(equals + 1);
                values[Decode(name)] = Decode(value);
            }
            return values;
        }

        // Splits "base?query#fragment" into "base", "query" (without the '?') and "#fragment"
        // (with the '#'). Missing parts come back as "".
        private static void SplitUrl(string url, out string beforeQuery, out string query, out string fragment)
        {
            url = url ?? "";
            int hash = url.IndexOf('#');
            fragment = hash < 0 ? "" : url.Substring(hash);
            string rest = hash < 0 ? url : url.Substring(0, hash);

            int question = rest.IndexOf('?');
            beforeQuery = question < 0 ? rest : rest.Substring(0, question);
            query = question < 0 ? "" : rest.Substring(question + 1);
        }

        // Query strings encode spaces as '+' (form encoding) or %20; handle both.
        private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
    }
}
