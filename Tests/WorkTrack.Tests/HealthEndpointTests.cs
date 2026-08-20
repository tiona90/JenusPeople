using System.Net;
using System.Text.Json;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A host whose mail settings point at a closed port, so the readiness probe's mail
/// check fails locally instead of opening a session with Gmail or Brevo every time
/// these tests run. The database is the in-memory store, which connects.
/// </summary>
public sealed class ProbeHostFixture : IDisposable
{
    private readonly ProductionHostFactory _factory = new(new Dictionary<string, string>
    {
        ["Email:Provider"] = "Smtp",
        ["MailSettings:Host"] = "127.0.0.1",
        ["MailSettings:Port"] = "1",
        ["MailSettings:Mail"] = string.Empty,
        ["MailSettings:Password"] = string.Empty,
    });

    public HttpClient Client { get; }

    public ProbeHostFixture() => Client = _factory.CreateClient();

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }
}

/// <summary>
/// The two probe endpoints, and the difference between them.
///
/// Liveness answers "is this process serving" and runs no checks, so it stays 200
/// through a dependency outage — a liveness probe that fails when the database does
/// gets the instance restarted, and restarting does not repair someone else's
/// database. Readiness answers "can this instance do its job" and runs the checks:
/// the database (fatal) and the configured mail provider (degraded, because late
/// notifications are not a reason to pull an instance out of the load balancer).
/// </summary>
public class HealthEndpointTests(ProbeHostFixture probeHost) : IClassFixture<ProbeHostFixture>
{
    private sealed record Probe(string Status, IReadOnlyList<Check> Checks);

    private sealed record Check(string Name, string Status);

    private static async Task<Probe> ReadProbeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;

        return new Probe(
            root.GetProperty("status").GetString() ?? string.Empty,
            [.. root.GetProperty("checks").EnumerateArray().Select(entry => new Check(
                entry.GetProperty("name").GetString() ?? string.Empty,
                entry.GetProperty("status").GetString() ?? string.Empty))]);
    }

    [Fact]
    public async Task Liveness_answers_healthy_and_runs_no_checks()
    {
        var response = await probeHost.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var probe = await ReadProbeAsync(response);

        Assert.Equal("Healthy", probe.Status);
        Assert.Empty(probe.Checks);
    }

    /// <summary>
    /// The mail provider is unreachable in this host, so readiness is Degraded — and
    /// Degraded still answers 200. What a dashboard alerts on is the named check, not
    /// the status code.
    /// </summary>
    [Fact]
    public async Task Readiness_reports_each_check_and_stays_ok_when_only_mail_is_down()
    {
        var response = await probeHost.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var probe = await ReadProbeAsync(response);

        Assert.Equal("Degraded", probe.Status);
        Assert.Equal("Healthy", probe.Checks.Single(c => c.Name == "database").Status);
        Assert.Equal("Degraded", probe.Checks.Single(c => c.Name == "email").Status);
    }

    /// <summary>
    /// Anyone on the network can read these, so the body carries statuses and nothing
    /// else. A failing check's description names a host, a login or a port; that
    /// belongs in the log.
    /// </summary>
    [Fact]
    public async Task Readiness_does_not_disclose_why_a_check_failed()
    {
        var body = await probeHost.Client.GetStringAsync("/health/ready");

        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The case that makes readiness worth having: the database is gone. Development,
    /// because startup refuses to boot at all outside it when the database cannot be
    /// migrated — that is the other half of the same story.
    /// </summary>
    [Fact]
    public async Task Readiness_answers_503_when_the_database_cannot_be_reached()
    {
        using var factory = new ProductionHostFactory(
            new Dictionary<string, string>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=127.0.0.1,1;Database=worktrack_nope;User Id=nobody;Password=nothing;"
                    + "TrustServerCertificate=True;Connect Timeout=2",
                ["Email:Provider"] = "Smtp",
                ["MailSettings:Host"] = "127.0.0.1",
                ["MailSettings:Port"] = "1",
            },
            "Development",
            keepConfiguredDatabase: true);

        using var client = factory.CreateClient();

        var liveness = await client.GetAsync("/health");
        var readiness = await client.GetAsync("/health/ready");

        // Alive: the process is up and answering, which is all liveness claims.
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);

        // Not ready: it cannot serve a request that touches data.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);

        var probe = await ReadProbeAsync(readiness);
        Assert.Equal("Unhealthy", probe.Status);
        Assert.Equal("Unhealthy", probe.Checks.Single(c => c.Name == "database").Status);
    }
}
