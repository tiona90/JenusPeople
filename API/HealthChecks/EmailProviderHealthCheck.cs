using Infrastructure.Services.Email;
using Infrastructure.Services.Email.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace API.HealthChecks;

/// <summary>
/// Readiness: can this instance reach the mail provider it is configured to use?
///
/// Which provider that is comes from "Email:Provider", the same way
/// <c>EmailService</c> chooses one, so the probe tests the transport that would
/// actually carry a notification: an authenticated SMTP connect for
/// <c>SmtpEmailProvider</c>, a GET /account for <c>BrevoEmailProvider</c>. Neither
/// sends a message.
///
/// A failure is <b>Degraded</b>, not Unhealthy — that is deliberate. Mail being down
/// means notifications are late; it does not mean this instance should be pulled out
/// of the load balancer, because booking leave, filling timesheets and every read in
/// the application still work. <c>/health/ready</c> therefore still answers 200, with
/// this check named as degraded in the body, which is what a dashboard should alert
/// on.
/// </summary>
public sealed class EmailProviderHealthCheck(
    IEnumerable<IEmailProvider> providers,
    IOptions<EmailOptions> emailOptions,
    EmailProbeCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        var providerType = emailOptions.Value.Provider;
        var provider = providers.FirstOrDefault(p => p.ProviderType == providerType);

        if (provider is null)
        {
            // Misconfiguration rather than an outage: nothing will ever send.
            return HealthCheckResult.Unhealthy($"No email provider is registered for {providerType}.");
        }

        var data = new Dictionary<string, object> { ["provider"] = providerType.ToString() };

        var reachable = await cache.GetOrProbeAsync(
            () => provider.TestConnectionAsync(cancellationToken));

        return reachable
            ? HealthCheckResult.Healthy(data: data)
            : HealthCheckResult.Degraded($"{providerType} did not accept a connection.", data: data);
    }
}

/// <summary>
/// Remembers the last mail probe for a few minutes.
///
/// Without it, every readiness probe opens an authenticated SMTP session or calls
/// Brevo's API — once every few seconds, forever, from each instance. That is enough
/// traffic to look like abuse to a mail provider, and Google in particular starts
/// refusing logins that repeat too often. The cost is staleness: a provider that has
/// just recovered reads as degraded until the entry expires.
/// </summary>
public sealed class EmailProbeCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _probedAt = DateTimeOffset.MinValue;
    private bool _reachable;

    public async Task<bool> GetOrProbeAsync(Func<Task<bool>> probe)
    {
        await _gate.WaitAsync();
        try
        {
            if (DateTimeOffset.UtcNow - _probedAt < Ttl)
            {
                return _reachable;
            }

            _reachable = await probe();
            _probedAt = DateTimeOffset.UtcNow;
            return _reachable;
        }
        finally
        {
            _gate.Release();
        }
    }
}
