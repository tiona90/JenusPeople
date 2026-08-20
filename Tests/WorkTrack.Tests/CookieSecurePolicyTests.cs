using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The session lives in the Identity auth cookie, and the cookie policy decides
/// whether that cookie is marked <c>Secure</c>. It used to be
/// <c>SameAsRequest</c> in every environment, which leaves the flag off whenever
/// the application sees a plain-HTTP request — an http binding on the IIS site is
/// enough — and a cookie without the flag is one the browser will send in
/// cleartext, where it can be read or injected.
///
/// This reads the policy out of the running host rather than re-deriving it, so
/// it is the object the CookiePolicyMiddleware actually applies. The fixture
/// boots the application in the Production environment.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class CookieSecurePolicyTests(ApiRouteTableFixture routeTable)
{
    private CookiePolicyOptions Policy => routeTable.Services
        .GetRequiredService<IOptions<CookiePolicyOptions>>()
        .Value;

    [Fact]
    public void Outside_development_cookies_are_marked_secure_regardless_of_request_scheme()
    {
        Assert.Equal(CookieSecurePolicy.Always, Policy.Secure);
    }

    /// <summary>
    /// Lax, not None: the cookie stays off cross-site POSTs, which is what keeps
    /// a form on another origin from acting as the signed-in user.
    /// </summary>
    [Fact]
    public void Cookies_are_at_least_same_site_lax()
    {
        Assert.Equal(SameSiteMode.Lax, Policy.MinimumSameSitePolicy);
    }
}
