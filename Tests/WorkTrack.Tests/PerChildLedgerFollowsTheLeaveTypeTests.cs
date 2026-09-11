using Application.Children.Queries;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The ledger a screen quotes has to describe the leave type the employee is
/// actually asking for.
///
/// It did not. <c>ChildProjection.ResolvePerChildLeaveTypeAsync</c> picks a single
/// winner across the whole application — active first, then lowest id — which was
/// sound only while exactly one type carried a per-child entitlement. Both
/// Maternity and Paternity Leave may carry one, and Maternity is seeded first, so
/// it won every time: a paternity request was quoted Maternity's weeks and
/// Maternity's cut-off age, while <c>PerChildLeaveBalanceCalculator</c> went on
/// enforcing against the request's own type. A child eligible to the API read as
/// aged out on screen, and 18 weeks read as 4.
///
/// So the query takes the leave type it is being asked about. With none named it
/// keeps the old behaviour, which is what the plain children list still wants.
/// </summary>
public class PerChildLedgerFollowsTheLeaveTypeTests
{
    private const string UserId = "employee-1";
    private const string ProfileId = "profile-1";
    private const int MaternityTypeId = 5;
    private const int PaternityTypeId = 6;

    /// <summary>
    /// The two types as the reported database had them, including the detail that
    /// made the bug visible: Maternity's lower id, and the two very different
    /// cut-off ages.
    /// </summary>
    private static async Task<AppDbContext> WorldAsync()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });
        db.Users.Add(new User { Id = UserId, UserName = "e@x.com", Email = "e@x.com", DisplayName = "Andreas" });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ProfileId, UserId = UserId, HasChildren = true,
            AnnualLeaveEntitlement = 25, LeaveBalance = 25,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = MaternityTypeId, Name = "Maternity Leave", IsActive = true, RequiresApproval = true,
            PerChildEntitlement = true,
            PerChildTotalWeeks = 4, PerChildWeeksPerYear = 4, ChildEligibleUntilAge = 4,
        });
        db.LeaveTypes.Add(new LeaveType
        {
            Id = PaternityTypeId, Name = "Paternity Leave", IsActive = true, RequiresApproval = true,
            PerChildEntitlement = true,
            PerChildTotalWeeks = 18, PerChildWeeksPerYear = 6, ChildEligibleUntilAge = 15,
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task AddChildAsync(AppDbContext db, string name, int ageYears)
    {
        db.Children.Add(new Child
        {
            EmployeeProfileId = ProfileId,
            Name = name,
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-ageYears).AddDays(-1),
        });
        await db.SaveChangesAsync();
    }

    private static Task<Application.Core.Result<Application.Children.DTOs.ChildLeaveEntitlementSummaryDto>>
        Query(AppDbContext db, int? leaveTypeId) =>
        new GetChildLeaveEntitlements.Handler(db).Handle(new GetChildLeaveEntitlements.Query
        {
            CallerUserId = UserId,
            LeaveTypeId = leaveTypeId,
        }, CancellationToken.None);

    [Fact]
    public async Task Paternity_leave_is_quoted_its_own_eighteen_weeks_not_maternitys_four()
    {
        await using var db = await WorldAsync();
        await AddChildAsync(db, "Kokos", ageYears: 1);

        var result = await Query(db, PaternityTypeId);

        var child = Assert.Single(result.Value!.Children);
        Assert.Equal(90, child.TotalDays);
        Assert.Equal(18.0m, child.TotalWeeks);
        Assert.Equal(30, child.ThisYearCapDays);
    }

    [Fact]
    public async Task Maternity_leave_is_quoted_its_own_four_weeks()
    {
        await using var db = await WorldAsync();
        await AddChildAsync(db, "Kokos", ageYears: 1);

        var result = await Query(db, MaternityTypeId);

        var child = Assert.Single(result.Value!.Children);
        Assert.Equal(20, child.TotalDays);
    }

    /// <summary>
    /// The symptom as reported: a 12-year-old is eligible for paternity leave
    /// (under 15) and not for maternity leave (under 4). The single-winner
    /// resolver called her ineligible for both.
    /// </summary>
    [Fact]
    public async Task Eligibility_is_judged_against_the_requested_types_cut_off_age()
    {
        await using var db = await WorldAsync();
        await AddChildAsync(db, "Maria", ageYears: 12);

        var forPaternity = await Query(db, PaternityTypeId);
        var forMaternity = await Query(db, MaternityTypeId);

        Assert.True(Assert.Single(forPaternity.Value!.Children).IsEligible);
        Assert.Equal(1, forPaternity.Value!.EligibleChildCount);

        Assert.False(Assert.Single(forMaternity.Value!.Children).IsEligible);
        Assert.Equal(0, forMaternity.Value!.EligibleChildCount);
    }

    /// <summary>
    /// The totals the summary panel quotes cover every eligible child, so two
    /// children under 15 are worth two lots of the per-child entitlement.
    /// </summary>
    [Fact]
    public async Task The_totals_cover_every_eligible_child()
    {
        await using var db = await WorldAsync();
        await AddChildAsync(db, "Maria", ageYears: 12);
        await AddChildAsync(db, "Kokos", ageYears: 1);

        var result = await Query(db, PaternityTypeId);

        Assert.Equal(2, result.Value!.EligibleChildCount);
        Assert.Equal(180, result.Value!.TotalRemainingDays);
    }

    /// <summary>
    /// A leave type that keeps no per-child ledger has nothing to report, rather
    /// than quietly falling back to whichever type does.
    /// </summary>
    [Fact]
    public async Task A_type_with_no_per_child_ledger_reports_nothing()
    {
        await using var db = await WorldAsync();
        db.LeaveTypes.Add(new LeaveType { Id = 1, Name = "Annual Leave", IsActive = true, AffectsBalance = true });
        await db.SaveChangesAsync();
        await AddChildAsync(db, "Kokos", ageYears: 1);

        var result = await Query(db, leaveTypeId: 1);

        Assert.Null(result.Value!.LeaveTypeId);
        Assert.Empty(result.Value!.Children);
    }

    /// <summary>
    /// Naming no type keeps the old single-winner behaviour, which the plain
    /// children list still relies on.
    /// </summary>
    [Fact]
    public async Task Naming_no_leave_type_falls_back_to_the_resolved_one()
    {
        await using var db = await WorldAsync();
        await AddChildAsync(db, "Kokos", ageYears: 1);

        var result = await Query(db, leaveTypeId: null);

        Assert.Equal(MaternityTypeId, result.Value!.LeaveTypeId);
    }
}
