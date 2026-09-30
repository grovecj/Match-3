using System.Text;
using Match3.Accounts;
using NUnit.Framework;

namespace Match3.Tests
{
    /// <summary>
    /// Edit Mode tests for reading the sign-in callback URL (<see cref="AuthCallback"/>),
    /// including rejecting a missing or tampered state, and for <see cref="JwtClaims"/>.
    /// </summary>
    public class AuthCallbackTests
    {
        private const string Page = "https://games.cartergrove.me/match3/";

        [Test]
        public void TryParse_PlainPage_IsNotACallback()
        {
            Assert.That(AuthCallback.TryParse(Page, out _), Is.False);
            Assert.That(AuthCallback.TryParse(Page + "?level=2", out _), Is.False);
            Assert.That(AuthCallback.TryParse("", out _), Is.False);
            Assert.That(AuthCallback.TryParse(null, out _), Is.False);
        }

        [Test]
        public void TryParse_ReadsCodeAndState()
        {
            Assert.That(AuthCallback.TryParse(Page + "?code=abc123&state=xyz", out AuthCallback callback), Is.True);

            Assert.That(callback.Code, Is.EqualTo("abc123"));
            Assert.That(callback.State, Is.EqualTo("xyz"));
            Assert.That(callback.Error, Is.Null);
        }

        [Test]
        public void TryParse_PercentDecodesValues()
        {
            AuthCallback.TryParse(Page + "?code=a%2Fb%3Dc&state=s+t%20u", out AuthCallback callback);

            Assert.That(callback.Code, Is.EqualTo("a/b=c"));
            Assert.That(callback.State, Is.EqualTo("s t u"));
        }

        [Test]
        public void TryParse_IgnoresTheFragment()
        {
            AuthCallback.TryParse(Page + "?state=xyz&code=abc#code=fake", out AuthCallback callback);

            Assert.That(callback.Code, Is.EqualTo("abc"));
        }

        [Test]
        public void Validate_MatchingState_IsAccepted()
        {
            AuthCallback.TryParse(Page + "?code=abc&state=xyz", out AuthCallback callback);

            Assert.That(callback.Validate("xyz"), Is.Null);
        }

        [Test]
        public void Validate_TamperedState_IsRejected()
        {
            AuthCallback.TryParse(Page + "?code=abc&state=attacker", out AuthCallback callback);

            Assert.That(callback.Validate("xyz"), Is.Not.Null);
        }

        [Test]
        public void Validate_StateThatDiffersOnlyInCase_IsRejected()
        {
            AuthCallback.TryParse(Page + "?code=abc&state=XYZ", out AuthCallback callback);

            Assert.That(callback.Validate("xyz"), Is.Not.Null);
        }

        [Test]
        public void Validate_MissingState_IsRejected()
        {
            AuthCallback.TryParse(Page + "?code=abc", out AuthCallback callback);

            Assert.That(callback.Validate("xyz"), Is.Not.Null);
        }

        [Test]
        public void Validate_NoSavedState_IsRejected()
        {
            // A code arriving when we never started a sign-in (someone else's link).
            AuthCallback.TryParse(Page + "?code=abc&state=xyz", out AuthCallback callback);

            Assert.That(callback.Validate(null), Is.Not.Null);
            Assert.That(callback.Validate(""), Is.Not.Null);
        }

        [Test]
        public void Validate_Cancelled_SaysSo()
        {
            Assert.That(AuthCallback.TryParse(Page + "?error=access_denied&state=xyz", out AuthCallback callback), Is.True);

            Assert.That(callback.Validate("xyz"), Does.Contain("cancelled"));
        }

        [Test]
        public void Validate_ErrorWithWrongState_ReportsTheStateProblem()
        {
            // An error from a callback we didn't start isn't trusted either.
            AuthCallback.TryParse(Page + "?error=access_denied&state=other", out AuthCallback callback);

            Assert.That(callback.Validate("xyz"), Does.Not.Contain("cancelled"));
        }

        [Test]
        public void RemoveCallbackParameters_LeavesTheBarePage()
        {
            Assert.That(AuthCallback.RemoveCallbackParameters(Page + "?code=abc&state=xyz&iss=https%3A%2F%2Fauth"),
                Is.EqualTo(Page));
            Assert.That(AuthCallback.RemoveCallbackParameters(Page + "?error=access_denied&error_description=No&state=xyz"),
                Is.EqualTo(Page));
        }

        [Test]
        public void RemoveCallbackParameters_KeepsOtherParametersAndTheFragment()
        {
            Assert.That(AuthCallback.RemoveCallbackParameters(Page + "?level=2&code=abc&debug&state=xyz#top"),
                Is.EqualTo(Page + "?level=2&debug#top"));
        }

        [Test]
        public void WithoutQuery_StripsQueryAndFragment()
        {
            Assert.That(AuthCallback.WithoutQuery(Page + "?code=abc#x"), Is.EqualTo(Page));
            Assert.That(AuthCallback.WithoutQuery(Page), Is.EqualTo(Page));
        }

        [Test]
        public void JwtClaims_ReadsThePayload()
        {
            string payload = Base64Url.Encode(Encoding.UTF8.GetBytes(
                "{\"sub\":\"1234\",\"name\":\"Zoë Player\",\"exp\":1900000000,\"aud\":\"match3\"}"));
            string jwt = "eyJhbGciOiJFUzI1NiJ9." + payload + ".c2lnbmF0dXJl";

            JwtClaims claims = JwtClaims.Read(jwt);

            Assert.That(claims.sub, Is.EqualTo("1234"));
            Assert.That(claims.name, Is.EqualTo("Zoë Player"));
            Assert.That(claims.exp, Is.EqualTo(1900000000L));
        }

        [Test]
        public void JwtClaims_NotAJwt_IsNull()
        {
            Assert.That(JwtClaims.Read(null), Is.Null);
            Assert.That(JwtClaims.Read("not-a-jwt"), Is.Null);
            Assert.That(JwtClaims.Read("a.!!!.c"), Is.Null);
        }
    }
}
