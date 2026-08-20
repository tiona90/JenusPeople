using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The "ClientPolicy" CORS policy sends <c>AllowCredentials</c>, so every origin it
/// accepts can read authenticated responses — the session cookie rides along. The
/// origin check used to allow any <c>localhost</c> host unconditionally, which meant
/// a page served from any port on a signed-in user's machine could call the
/// deployed API and read the answers. localhost is a development convenience now
/// (the Vite dev server), and outside Development the "Cors:AllowedOrigins" list is
/// the only way past the check.
///
/// These tests preflight against the fixture's host, which boots the real
/// application in the Production environment.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class CorsOriginPolicyTests(ApiRouteTableFixture routeTable)
{
    /// <summary>Origins a non-Development host must not hand credentials to.</summary>
    public static TheoryData<string> RejectedOrigins() =>
    [
        "http://localhost:5173",
        "https://localhost",
        "http://LOCALHOST:3000",
        "https://evil.example.com",
    ];

    [Theory]
    [MemberData(nameof(RejectedOrigins))]
    public async Task Outside_development_a_localhost_origin_is_not_allowed(string origin)
    {
        var response = await Preflight(origin);

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            $"{origin} was allowed by CORS outside Development. Allow-Origin: "
            + string.Join(
                ", ",
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
                    ? values
                    : []));
    }

    [Fact]
    public async Task A_configured_origin_is_still_allowed()
    {
        var configured = routeTable.Services
            .GetRequiredService<IConfiguration>()
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();

        Assert.NotNull(configured);
        Assert.NotEmpty(configured);

        foreach (var origin in configured)
        {
            var response = await Preflight(origin);

            Assert.True(
                response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed)
                    && allowed.Contains(origin),
                $"{origin} comes from Cors:AllowedOrigins but CORS did not allow it.");
        }
    }

    /// <summary>
    /// A browser preflight: OPTIONS plus Origin and Access-Control-Request-Method.
    /// The CORS middleware answers it before the request reaches any endpoint, so
    /// the path only has to exist.
    /// </summary>
    private async Task<HttpResponseMessage> Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/Account/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        return await routeTable.Client.SendAsync(request);
    }
}
