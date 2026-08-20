using System.Text.Json;
using API.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace API.Extensions;

/// <summary>
/// The two probe endpoints, and the difference between them.
///
/// <c>/health</c> is liveness: the process is up and serving. It runs no checks at
/// all, so it answers while the database is down — which is the point. A liveness
/// probe that fails on a dependency outage gets the instance restarted, and
/// restarting does not fix someone else's database.
///
/// <c>/health/ready</c> is readiness: this instance can do its job. It runs the
/// checks tagged "ready" — the database, and reachability of the configured mail
/// provider.
///
/// Both are anonymous, because a probe has no account. Neither returns exception
/// messages or connection strings: the body names each check and its status and
/// nothing else, since anyone on the network can read it.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>Checks that answer "is this instance ready", as opposed to "alive".</summary>
    private const string ReadyTag = "ready";

    public const string LivenessPath = "/health";
    public const string ReadinessPath = "/health/ready";

    public static IServiceCollection AddHealthCheckEndpoints(this IServiceCollection services)
    {
        // Shared across probes so a mail provider is not contacted on every one.
        services.AddSingleton<EmailProbeCache>();

        services.AddHealthChecks()
            // Timeouts, because a probe that hangs is worse than one that fails: the
            // orchestrator waits instead of acting.
            .AddCheck<DatabaseHealthCheck>(
                "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<EmailProviderHealthCheck>(
                "email",
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyTag],
                timeout: TimeSpan.FromSeconds(10));

        return services;
    }

    public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            // No checks: alive is alive.
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync,
        })
        // A probe runs every few seconds and shares an address with everything else
        // behind the proxy. Left rate-limited, it would spend the caller's budget
        // and eventually 429 — and an orchestrator reads 429 as "not healthy".
        .DisableRateLimiting();

        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponseAsync,
        })
        .DisableRateLimiting();

        return app;
    }

    /// <summary>
    /// True for the probe endpoints, so request logging can keep them quiet.
    /// </summary>
    public static bool IsHealthPath(PathString path) =>
        path.StartsWithSegments(LivenessPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Status names only. Descriptions and exceptions stay in the log, where the
    /// server name and login in a connection failure are not served to whoever asked.
    /// </summary>
    private static async Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                })
                .OrderBy(entry => entry.name, StringComparer.Ordinal)
                .ToArray(),
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(payload),
            context.RequestAborted);
    }
}
