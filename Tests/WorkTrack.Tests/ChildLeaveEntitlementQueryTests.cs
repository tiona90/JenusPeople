using Application.Children.Queries;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The ledger the UI quotes. It must agree with the calculator that enforces the
/// caps to the day — a screen promising 13 weeks while the API refuses at 8 is worse
/// than no screen — so both read usage through the same helper.
///
/// The employee's totals cover eligible children only. That is requirement 10 with
/// no code: a child aging out changes the projection, so the figures fall on the next
/// read whether or not that child had used leave.
/// </summary>
public class ChildLeaveEntitlementQueryTests
{
    private static readonly DateOnly YoungChild = new(2019, 3, 4);

    private static Task<Application.Core.Result<Application.Children.DTOs.ChildLeaveEntitlementSummaryDto>> Query(
        Persistence.AppDbContext db) =>
        new GetChildLeaveEntitlements.Handler(db).Handle(new GetChildLeaveEntitlements.Query
        {
            CallerUserId = PerChildLeaveWorld.UserId,
        }, CancellationToken.None);

    [Fact]
    public async Task An_untouched_child_has_the_full_eighteen_weeks()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        var result = await Query(db);

        Assert.True(result.IsSuccess);
        var child = Assert.Single(result.Value!.Children);
        Assert.Equal(90, child.TotalDays);
        Assert.Equal(18.0m, child.TotalWeeks);
        Assert.Equal(0, child.UsedDays);
        Assert.Equal(90, child.RemainingDays);
        Assert.Equal(25, child.ThisYearCapDays);
        Assert.Equal(25, child.ThisYearRemainingDays);
    }

    [Fact]
    public async Task Used_days_come_off_both_the_total_and_the_year()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 5 business days in the current leave year. DateTime.UtcNow decides which
        // year that is, so anchor to it rather than to a literal 2026.
        var monday = ThisYearsMonday();
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, monday, monday.AddDays(4));

        var result = await Query(db);

        var row = Assert.Single(result.Value!.Children);
        Assert.Equal(5, row.UsedDays);
        Assert.Equal(85, row.RemainingDays);
        Assert.Equal(5, row.ThisYearUsedDays);
        Assert.Equal(20, row.ThisYearRemainingDays);
    }

    /// <summary>
    /// Requirement 10. Three children, one of them 15: the totals describe two.
    /// The aged-out child is still listed, with isEligible false, so the UI can say
    /// why rather than silently dropping them.
    /// </summary>
    [Fact]
    public async Task Totals_cover_eligible_children_only()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);
        await PerChildLeaveWorld.AddChildAsync(db, "Maria", new DateOnly(2021, 6, 15));
        // Born well over 15 years ago whenever this runs.
        var agedOut = await PerChildLeaveWorld.AddChildAsync(
            db, "Petros", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-16));

        // The aged-out child used leave when they were eligible. It stays in
        // history and must not resurrect their entitlement.
        var monday = ThisYearsMonday();
        await PerChildLeaveWorld.ApproveLeaveAsync(db, agedOut.Id, monday, monday.AddDays(4));

        var result = await Query(db);
        var summary = result.Value!;

        Assert.Equal(3, summary.Children.Count);
        Assert.Equal(2, summary.EligibleChildCount);
        // Two eligible children, untouched: 2 x 90 days.
        Assert.Equal(180, summary.TotalRemainingDays);
        // 2 x 25 days this leave year.
        Assert.Equal(50, summary.ThisYearCapDays);

        var petros = summary.Children.Single(c => c.Name == "Petros");
        Assert.False(petros.IsEligible);
        Assert.Equal(5, petros.UsedDays);
        Assert.Equal(0, petros.RemainingDays);
        Assert.Equal(0, petros.ThisYearRemainingDays);
    }

    [Fact]
    public async Task Without_a_per_child_leave_type_the_ledger_is_empty()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var paternity = await db.LeaveTypes.FindAsync(PerChildLeaveWorld.PaternityTypeId);
        paternity!.PerChildEntitlement = false;
        await db.SaveChangesAsync();

        await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        var result = await Query(db);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.LeaveTypeId);
        Assert.Empty(result.Value.Children);
    }

    /// <summary>The Monday of the first full week of the current calendar year.</summary>
    private static DateTime ThisYearsMonday()
    {
        var date = new DateTime(DateTime.UtcNow.Year, 1, 1);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }
}
