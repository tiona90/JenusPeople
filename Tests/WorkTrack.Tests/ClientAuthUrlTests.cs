using System.Reflection;
using API.Controllers;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Links emailed to users must point at a real client route.
///
/// The SPA routes on the path (react-router). An earlier hash-based form —
/// <c>{base}/?email=…&amp;token=…#reset-password</c> — resolves to <c>/</c>, which
/// bounces an unauthenticated visitor to <c>/login</c> and drops the query
/// string, so password-reset emails opened the sign-in page with the token
/// discarded and no way to set a new password.
/// </summary>
public class ClientAuthUrlTests
{
    private const string ClientBaseUrl = "https://people.example.test";

    /// <summary>
    /// BuildClientAuthUrl reads only <c>appUrlOptions</c>, so the remaining
    /// primary-constructor dependencies are left null deliberately.
    /// </summary>
    private static string BuildUrl(string route, IDictionary<string, string?>? query = null, string? baseUrl = ClientBaseUrl)
    {
        var options = Options.Create(new AppUrlOptions { ClientBaseUrl = baseUrl! });
        var controller = new AccountController(null!, null!, null!, options, null!, null!, null!);

        var method = typeof(AccountController).GetMethod(
            "BuildClientAuthUrl",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        return (string)method!.Invoke(controller, [route, query])!;
    }

    [Fact]
    public void Reset_password_link_targets_the_reset_password_route()
    {
        var url = BuildUrl("reset-password", new Dictionary<string, string?>
        {
            ["email"] = "person@example.test",
            ["token"] = "AbC-123_xyz",
        });

        // '@' is left as-is: it is a legal query character (RFC 3986), and the
        // base64url token alphabet needs no escaping either.
        Assert.Equal(
            $"{ClientBaseUrl}/reset-password?email=person@example.test&token=AbC-123_xyz",
            url);
    }

    /// <summary>
    /// The regression itself: a '#' would put the route in the fragment, which
    /// never reaches the router as a path.
    /// </summary>
    [Fact]
    public void Links_never_put_the_route_in_the_fragment()
    {
        foreach (var route in new[] { "login", "reset-password" })
        {
            var url = BuildUrl(route, new Dictionary<string, string?> { ["token"] = "t" });

            Assert.DoesNotContain("#", url);
            Assert.Contains($"/{route}?", url);
        }
    }

    [Fact]
    public void Query_values_are_escaped_so_tokens_survive_intact()
    {
        var url = BuildUrl("reset-password", new Dictionary<string, string?>
        {
            ["authMessage"] = "Your account is ready. Sign in & continue?",
        });

        Assert.Contains("authMessage=Your%20account%20is%20ready.%20Sign%20in%20%26%20continue%3F", url);
    }

    [Fact]
    public void A_trailing_slash_on_the_configured_base_url_does_not_double_up()
    {
        var url = BuildUrl("login", query: null, baseUrl: $"{ClientBaseUrl}/");

        Assert.Equal($"{ClientBaseUrl}/login", url);
    }

    [Theory]
    [InlineData("#reset-password")]
    [InlineData("/reset-password")]
    public void Callers_may_pass_a_leading_hash_or_slash(string route)
    {
        Assert.Equal($"{ClientBaseUrl}/reset-password", BuildUrl(route));
    }
}
