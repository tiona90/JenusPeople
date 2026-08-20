using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Startup applies migrations, and a failure there used to be logged and shrugged
/// off in every environment. A deploy against a database the app could not reach —
/// wrong password, firewall, a migration that will not apply — came up healthy and
/// then failed requests one query at a time, which reads as a flaky application
/// rather than as the broken deploy it is. Worse, the log line said "An error
/// accoured duaring migration", which is not a string anyone greps for.
///
/// Outside Development the exception is rethrown now, so the host does not start.
/// Development still boots, because working offline on the parts that need no
/// database is worth keeping.
///
/// Both hosts here keep the SQL Server provider from configuration and point it at
/// a port nothing listens on, which is what "the database is unreachable" looks
/// like from inside startup.
/// </summary>
public class StartupMigrationFailureTests
{
    /// <summary>
    /// Port 1: the connection is refused rather than timing out, so these tests do
    /// not sit waiting. Connect Timeout is short in case a host answers anyway.
    /// </summary>
    private const string UnreachableDatabase =
        "Server=127.0.0.1,1;Database=worktrack_nope;User Id=nobody;Password=nothing;"
        + "TrustServerCertificate=True;Connect Timeout=2";

    private static ProductionHostFactory HostFor(string environment) => new(
        new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = UnreachableDatabase,
        },
        environment,
        keepConfiguredDatabase: true);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Outside_development_an_unreachable_database_stops_the_host(string environment)
    {
        using var factory = HostFor(environment);

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    /// <summary>
    /// The developer-machine case: no SQL Server running, and the application still
    /// starts so the rest of it can be worked on.
    ///
    /// Development is the one environment that layers user secrets over the
    /// configuration, so a machine that keeps a DefaultConnection there boots
    /// against a database that answers, and this test then proves only that
    /// startup survived. The environments above do not read user secrets, so the
    /// failure they assert on is not machine-dependent.
    /// </summary>
    [Fact]
    public void Development_still_boots_when_the_database_is_unreachable()
    {
        using var factory = HostFor("Development");

        using var client = factory.CreateClient();

        Assert.NotNull(client);
    }
}
