using System.Net;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The two cross-timesheet status-history endpoints carried absolute route
/// templates — <c>/api/admin/timesheets/history</c> and
/// <c>/api/employees/{id}/timesheets/history</c>. A leading slash opts an action
/// out of its controller's <c>[Route]</c> attributes, and those are where this API
/// gets both its versioned <c>api/v{version}/…</c> path and the unversioned alias.
/// So the two endpoints sat outside versioning altogether: there was no
/// <c>/api/v1/…</c> spelling of either, and no way to revise them behind a new API
/// version the way every other action can be.
///
/// Both templates are relative now. These tests read the running application's
/// endpoint table rather than the controller's attributes, because the question is
/// what the router will actually match.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class TimesheetHistoryRouteVersioningTests(ApiRouteTableFixture routeTable)
{
    private const string AllHistories = "api/Timesheets/history";
    private const string EmployeeHistories = "api/Timesheets/employees/{employeeProfileId}/history";

    /// <summary>
    /// The absolute templates these replaced. Nothing should answer on them.
    /// </summary>
    public static TheoryData<string> RetiredPaths() =>
    [
        "/api/admin/timesheets/history",
        "/api/employees/some-profile-id/timesheets/history",
    ];

    public static TheoryData<string> ServedPaths() =>
    [
        "/api/timesheets/history",
        "/api/v1/timesheets/history",
        "/api/timesheets/employees/some-profile-id/history",
        "/api/v1/timesheets/employees/some-profile-id/history",
    ];

    private List<RouteEntry> RoutesFor(string unversionedPattern) =>
    [
        .. routeTable.Routes.Where(r =>
            r.UnversionedPattern.Equals(unversionedPattern, StringComparison.OrdinalIgnoreCase)),
    ];

    [Theory]
    [InlineData(AllHistories)]
    [InlineData(EmployeeHistories)]
    public void The_history_endpoints_are_mapped_both_versioned_and_unversioned(string pattern)
    {
        var matches = RoutesFor(pattern);

        Assert.True(
            matches.Count == 2,
            $"Expected a versioned and an unversioned route for {pattern}, found "
            + $"{matches.Count}: {string.Join(", ", matches.Select(m => m.Describe()))}. "
            + $"All GET routes under api/Timesheets: {string.Join(", ", routeTable.Routes
                .Where(r => r.Pattern.Contains("Timesheets", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ToString()))}");

        Assert.Single(matches, m => m.Pattern.Contains("v{version:apiVersion}/", StringComparison.Ordinal));
        Assert.Single(matches, m => !m.Pattern.Contains("v{version:apiVersion}/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Neither endpoint may be anonymous: one is admin-only, and the other leans
    /// entirely on the handler's scope filter, which needs to know who is asking.
    /// </summary>
    [Theory]
    [InlineData(AllHistories)]
    [InlineData(EmployeeHistories)]
    public void The_history_endpoints_require_authentication(string pattern)
    {
        var matches = RoutesFor(pattern);

        Assert.NotEmpty(matches);
        Assert.All(matches, m => Assert.False(m.AllowsAnonymous, m.Describe()));
    }

    [Fact]
    public void No_route_keeps_an_absolute_history_template()
    {
        var offending = routeTable.Routes
            .Where(r => r.Pattern.Contains("timesheets/history", StringComparison.OrdinalIgnoreCase)
                && !r.Pattern.StartsWith("api/Timesheets/", StringComparison.OrdinalIgnoreCase)
                && !r.Pattern.StartsWith("api/v{version:apiVersion}/Timesheets/", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Describe())
            .ToList();

        Assert.True(offending.Count == 0, $"Absolute history routes still mapped: {string.Join(", ", offending)}");
    }

    /// <summary>
    /// 401 rather than 404 proves the path routes to a real endpoint and that
    /// authorization — not a missing route — is what turns the anonymous probe
    /// away. It also proves the router settled on exactly one endpoint: the
    /// collection-level <c>history</c> segment overlaps <c>{id}</c> from
    /// GET api/timesheets/{id}, and an ambiguous match would surface as a 500.
    /// </summary>
    [Theory]
    [MemberData(nameof(ServedPaths))]
    public async Task The_history_paths_are_served(string path)
    {
        var response = await routeTable.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(RetiredPaths))]
    public async Task The_retired_absolute_paths_are_not_served(string path)
    {
        var response = await routeTable.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
