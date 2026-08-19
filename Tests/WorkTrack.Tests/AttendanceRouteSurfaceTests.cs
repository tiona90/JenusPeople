using System.Collections.Immutable;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// AttendanceController went from 765 lines of inline logic to 125 lines of
/// dispatch. Every rule it used to hold is now covered by unit tests, but the one
/// thing those cannot catch is the refactor moving or dropping a route — the SPA
/// calls these ten paths by hand, and a renamed segment would surface as a dead
/// feature rather than a failing test.
///
/// So this pins the surface by exact equality against the running application's
/// endpoint table: ten actions, each mapped twice (versioned and unversioned),
/// none anonymous.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class AttendanceRouteSurfaceTests(ApiRouteTableFixture routeTable)
{
    /// <summary>
    /// Method and unversioned path for every attendance endpoint, exactly as the
    /// client calls them. See client/src/lib/api/attendance.ts.
    /// </summary>
    private static readonly ImmutableArray<string> Expected =
    [
        "GET /api/Attendance/company",
        "GET /api/Attendance/me/history",
        "GET /api/Attendance/me/today",
        "GET /api/Attendance/presence",
        "GET /api/Attendance/team",
        "GET /api/Attendance/team/history",
        "POST /api/Attendance/break/end",
        "POST /api/Attendance/break/start",
        "POST /api/Attendance/check-in",
        "POST /api/Attendance/check-out",
    ];

    private List<RouteEntry> AttendanceRoutes =>
    [
        .. routeTable.Routes.Where(r =>
            r.UnversionedPattern.StartsWith("api/Attendance/", StringComparison.OrdinalIgnoreCase)),
    ];

    [Fact]
    public void The_attendance_surface_is_exactly_the_ten_documented_endpoints()
    {
        var actual = AttendanceRoutes
            .Select(r => $"{string.Join(",", r.HttpMethods)} /{r.UnversionedPattern}")
            .Distinct()
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(Expected, actual);
    }

    /// <summary>
    /// Each action has to pick up both of BaseApiController's route attributes. An
    /// absolute template would give it only one and take it outside versioning.
    /// </summary>
    [Fact]
    public void Every_attendance_endpoint_is_mapped_versioned_and_unversioned()
    {
        var byPath = AttendanceRoutes.GroupBy(r => r.UnversionedPattern, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(Expected.Length, byPath.Count());
        Assert.All(byPath, group =>
        {
            Assert.Single(group, r => r.Pattern.Contains("v{version:apiVersion}/", StringComparison.Ordinal));
            Assert.Single(group, r => !r.Pattern.Contains("v{version:apiVersion}/", StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// Attendance is recorded against the caller, so none of these can be reached
    /// without knowing who that is. The controller-level [Authorize] covers all
    /// ten; the role-restricted ones are checked separately below.
    /// </summary>
    [Fact]
    public void No_attendance_endpoint_is_anonymous()
    {
        var anonymous = AttendanceRoutes
            .Where(r => r.AllowsAnonymous)
            .Select(r => r.Describe())
            .ToList();

        Assert.True(anonymous.Count == 0, $"Anonymous attendance routes: {string.Join(", ", anonymous)}");
    }

    /// <summary>
    /// The team board and its history are Admin-or-Manager; presence and the
    /// company dashboard are Admin only. Replacing the "Admin,Manager" string
    /// literals with AppRoles constants must not have changed which roles reach
    /// which route.
    /// </summary>
    [Theory]
    [InlineData("api/Attendance/team", "Admin,Manager")]
    [InlineData("api/Attendance/team/history", "Admin,Manager")]
    [InlineData("api/Attendance/presence", "Admin")]
    [InlineData("api/Attendance/company", "Admin")]
    public void The_role_restricted_endpoints_keep_their_roles(string pattern, string expectedRoles)
    {
        var matches = routeTable.Routes
            .Where(r => r.UnversionedPattern.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(matches);
        Assert.All(matches, r => Assert.Equal(expectedRoles, r.Roles));
    }

    /// <summary>
    /// The four self-service routes carry no role requirement of their own — any
    /// authenticated employee reaches them, and the handler resolves their profile.
    /// </summary>
    [Theory]
    [InlineData("api/Attendance/me/today")]
    [InlineData("api/Attendance/me/history")]
    [InlineData("api/Attendance/check-in")]
    [InlineData("api/Attendance/check-out")]
    public void The_self_service_endpoints_are_not_role_restricted(string pattern)
    {
        var matches = routeTable.Routes
            .Where(r => r.UnversionedPattern.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(matches);
        Assert.All(matches, r => Assert.Null(r.Roles));
    }
}
