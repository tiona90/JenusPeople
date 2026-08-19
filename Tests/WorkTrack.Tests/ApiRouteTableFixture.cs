using System.Collections.Immutable;
using API.BackgroundServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Persistence;
using Persistence.Interceptors;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// One entry in the running application's endpoint table.
/// </summary>
/// <param name="Pattern">
/// The route template with tokens already substituted, e.g. <c>api/Account/login</c>.
/// </param>
/// <param name="HttpMethods">
/// Methods the endpoint answers, or empty when it accepts any (as the SPA
/// fallback does).
/// </param>
/// <param name="AllowsAnonymous">
/// Whether an unauthenticated caller reaches the handler.
/// </param>
public sealed record RouteEntry(
    string Pattern,
    ImmutableArray<string> HttpMethods,
    bool AllowsAnonymous,
    string DisplayName)
{
    /// <summary>
    /// Every controller carries both a versioned and an unversioned route
    /// attribute, so each action appears twice in the table. Folding
    /// <c>api/v{version:apiVersion}/…</c> onto <c>api/…</c> lets a test name the
    /// surface once instead of listing both spellings of every route.
    /// </summary>
    public string UnversionedPattern =>
        Pattern.Replace("v{version:apiVersion}/", string.Empty, StringComparison.Ordinal);

    /// <summary>Method, path and handler — what a failure message needs.</summary>
    public string Describe() => $"{this} → {DisplayName}";

    public override string ToString() =>
        HttpMethods.IsEmpty
            ? $"* /{Pattern}"
            : $"{string.Join(",", HttpMethods)} /{Pattern}";
}

/// <summary>
/// Boots the real API in-process and captures its endpoint table.
///
/// Reflecting over controller attributes cannot see minimal-API routes, which is
/// exactly where <c>MapIdentityApi</c>'s anonymous <c>POST /api/register</c> used
/// to live. Reading <see cref="EndpointDataSource"/> from the running host sees
/// everything the router will actually match — controllers, hubs, minimal APIs
/// and the SPA fallback alike.
///
/// The host is started against an in-memory store, with seeding off and the
/// connection string blanked. That is not tidiness: appsettings.Production.json
/// points DefaultConnection at a remote server, so a fixture that booted Production
/// as configured would run migrations against a live database. The startup
/// migration then fails against the in-memory provider and is swallowed by the
/// try/catch in Program.cs, which is fine — routing needs no database.
/// </summary>
public sealed class ApiRouteTableFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    /// <summary>The application's complete route table.</summary>
    public ImmutableArray<RouteEntry> Routes { get; private set; } = [];

    /// <summary>
    /// Talks to the in-process server. Owned by the fixture — callers must not
    /// dispose it.
    /// </summary>
    public HttpClient Client => _client
        ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public Task InitializeAsync()
    {
        _factory = new RouteTableFactory();

        // Creating the client starts the host and builds the endpoint middleware;
        // the route table is not populated until then.
        _client = _factory.CreateClient();

        var endpoints = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints;

        Routes = [.. endpoints.Select(Describe).OrderBy(r => r.ToString(), StringComparer.Ordinal)];

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private static RouteEntry Describe(Endpoint endpoint)
    {
        var pattern = endpoint is RouteEndpoint route
            ? route.RoutePattern.RawText ?? string.Empty
            : string.Empty;

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
            ?? (IReadOnlyList<string>)[];

        // How the authorization middleware decides: any IAuthorizeData on the
        // endpoint means authorization runs, unless IAllowAnonymous is also
        // present — that is what lets [AllowAnonymous] on an action override
        // [Authorize] on its controller. There is no fallback policy configured,
        // so an endpoint with no authorization metadata at all is anonymous.
        var allowsAnonymous =
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || !endpoint.Metadata.OfType<IAuthorizeData>().Any();

        return new RouteEntry(
            pattern,
            [.. methods.OrderBy(m => m, StringComparer.Ordinal)],
            allowsAnonymous,
            endpoint.DisplayName ?? pattern);
    }

    private sealed class RouteTableFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Production is the configuration that ships, and the one whose
            // anonymous surface matters. It also keeps the Swagger UI's routes
            // out of the table, since Program.cs maps those only in Development.
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:DefaultConnection", string.Empty);
            builder.UseSetting("Seed:Enabled", "false");
            builder.UseSetting("Seed:DemoData", "false");

            // Production pins AllowedHosts to the deployed domain, and
            // TestServer sends "Host: localhost", so host filtering would answer
            // every probe 400 before routing ran. Which hostnames are accepted is
            // a separate concern from which routes exist.
            builder.UseSetting("AllowedHosts", "*");

            builder.ConfigureServices(services =>
            {
                // Point AppDbContext at an in-memory store so nothing in the host
                // can open a connection to the SQL Server named in the config.
                //
                // Dropping DbContextOptions alone is not enough: AddDbContext also
                // registers the options callback itself (as
                // IDbContextOptionsConfiguration<AppDbContext> on current EF), and
                // leaving that behind applies UseSqlServer on top of
                // UseInMemoryDatabase — EF then refuses to build the context at
                // all, complaining that two providers are registered. Sweeping
                // every AppDbContext-parameterised service takes the callback with
                // it without this fixture having to name EF's internal types.
                foreach (var registration in services
                    .Where(s => s.ServiceType == typeof(AppDbContext)
                        || s.ServiceType == typeof(DbContextOptions)
                        || (s.ServiceType.IsGenericType
                            && s.ServiceType.GenericTypeArguments.Contains(typeof(AppDbContext))))
                    .ToList())
                {
                    services.Remove(registration);
                }

                services.AddDbContext<AppDbContext>((sp, options) => options
                    .UseInMemoryDatabase($"route-table-{Guid.NewGuid()}")
                    .AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>()));

                // Nothing about routing needs the reminder scheduler, and it wakes
                // up immediately on startup.
                foreach (var hostedService in services
                    .Where(s => s.ServiceType == typeof(IHostedService)
                        && s.ImplementationType == typeof(ReminderBackgroundService))
                    .ToList())
                {
                    services.Remove(hostedService);
                }
            });
        }
    }
}
