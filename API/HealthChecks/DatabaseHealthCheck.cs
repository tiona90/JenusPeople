using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Persistence;

namespace API.HealthChecks;

/// <summary>
/// Readiness: can this instance reach its database?
///
/// Nothing this application does works without it — every page load reads the
/// signed-in user's profile — so a failure here is Unhealthy, and
/// <c>/health/ready</c> answers 503. A load balancer should stop sending traffic to
/// an instance in that state.
///
/// <c>CanConnectAsync</c> opens a connection and closes it. It deliberately does not
/// check whether migrations are up to date: startup already refuses to run against a
/// database it could not migrate (see Program.cs), so a running instance has the
/// schema, and asking again on every probe would query the history table for nothing.
/// </summary>
public sealed class DatabaseHealthCheck(AppDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database refused a connection.");
        }
        catch (Exception ex)
        {
            // Reported rather than thrown: the exception message names the server
            // and login, which does not belong in a response body served
            // anonymously. It reaches the log through the exception object.
            return HealthCheckResult.Unhealthy("The database could not be reached.", ex);
        }
    }
}
