using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A host booted with "ForwardedHeaders:KnownProxies" naming one trusted proxy.
/// Shared by the tests below so the application is only started once.
/// </summary>
public sealed class ProxyAwareHostFixture : IDisposable
{
    /// <summary>The address the configured host trusts to speak for others.</summary>
    public const string TrustedProxy = "10.0.0.9";

    private readonly ProductionHostFactory _factory = new(new Dictionary<string, string>
    {
        ["ForwardedHeaders:KnownProxies:0"] = TrustedProxy,
    });

    public ProxyAwareHostFixture() => _ = _factory.CreateClient();

    public TestServer Server => _factory.Server;

    public void Dispose() => _factory.Dispose();
}

/// <summary>
/// The rate limiters partition by <c>Connection.RemoteIpAddress</c>. Put a reverse
/// proxy in front of the application and that address is the proxy's for every
/// caller, so one partition covers the internet — 100 requests a minute for
/// everybody, and five login attempts a minute any single client can spend.
/// <c>UseForwardedHeaders</c> restores the real address from X-Forwarded-For.
///
/// The catch is that X-Forwarded-For is a request header, so trusting it from a
/// peer that is not a proxy is worse than the problem: every caller then picks its
/// own partition key and neither limiter counts anything. So the middleware is
/// registered only when proxies are configured, and it trusts only those
/// addresses. The current IIS/ANCM in-process deployment has no such hop and
/// configures none.
///
/// These drive the real pipeline through TestServer, which is the only way to set
/// the calling address an HttpClient cannot control.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class ForwardedHeadersTests(ApiRouteTableFixture routeTable, ProxyAwareHostFixture proxyHost)
    : IClassFixture<ProxyAwareHostFixture>
{
    /// <summary>The address a caller claims to be, via X-Forwarded-For.</summary>
    private const string ClaimedClient = "203.0.113.7";

    /// <summary>An address the configured host has no reason to trust.</summary>
    private const string Stranger = "198.51.100.5";

    /// <summary>
    /// A request that arrives from <paramref name="from"/> carrying forwarded
    /// headers. The path only has to exist — 401 is a fine answer; what the tests
    /// read is the connection the pipeline ended up seeing.
    /// </summary>
    private static Action<HttpContext> Arriving(string from) => context =>
    {
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/Account/user-info";
        context.Request.Headers["X-Forwarded-For"] = ClaimedClient;
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Connection.RemoteIpAddress = IPAddress.Parse(from);
    };

    [Fact]
    public async Task With_no_proxies_configured_a_forwarded_header_cannot_change_the_client_address()
    {
        var context = await routeTable.Server.SendAsync(
            Arriving(ProxyAwareHostFixture.TrustedProxy));

        Assert.Equal(
            IPAddress.Parse(ProxyAwareHostFixture.TrustedProxy),
            context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task A_request_through_a_configured_proxy_is_attributed_to_the_forwarded_client()
    {
        var context = await proxyHost.Server.SendAsync(
            Arriving(ProxyAwareHostFixture.TrustedProxy));

        Assert.Equal(IPAddress.Parse(ClaimedClient), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    /// <summary>
    /// The point of KnownProxies: the same headers from anyone else are ignored,
    /// so a caller cannot hand itself a fresh rate-limit partition per request.
    /// </summary>
    [Fact]
    public async Task The_same_headers_from_an_untrusted_address_are_ignored()
    {
        var context = await proxyHost.Server.SendAsync(Arriving(Stranger));

        Assert.Equal(IPAddress.Parse(Stranger), context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    /// <summary>
    /// A mistyped address must not degrade to "trust nobody" quietly — that looks
    /// exactly like the shared-partition bug the setting exists to fix.
    /// </summary>
    [Fact]
    public void A_proxy_address_that_is_not_an_ip_stops_startup()
    {
        using var factory = new ProductionHostFactory(new Dictionary<string, string>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "proxy.internal",
        });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("proxy.internal", error.ToString(), StringComparison.Ordinal);
    }
}
