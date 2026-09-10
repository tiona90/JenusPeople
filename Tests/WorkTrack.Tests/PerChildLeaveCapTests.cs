using Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The two caps: 18 weeks (90 business days) per child for their whole eligible
/// life, and 5 weeks (25 business days) per child per leave year.
///
/// A week is five business days, so weekends and public holidays inside a request
/// do not consume entitlement. Only Approved leave counts as used — the same rule
/// the pooled annual-leave balance already applies.
/// </summary>
public class PerChildLeaveCapTests
{
    // Born 2019: under 15 for every date in these tests.
    private static readonly DateOnly YoungChild = new(2019, 3, 4);

    /// <summary>
    /// Requirement example 1. Five weeks in one leave year, two in the next, three
    /// in the one after: 10 of 18 weeks used, 8 (40 business days) left.
    /// </summary>
    [Fact]
    public async Task Example_one_tracks_what_is_left_across_leave_years()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // Year 1: 5 weeks (25 business days), Mon 05 Jan - Fri 06 Feb 2026.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        // Year 2: 2 weeks (10 business days), Mon 04 Jan - Fri 15 Jan 2027.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2027, 1, 4), new DateTime(2027, 1, 15));
        // Year 3: 3 weeks (15 business days), Mon 03 Jan - Fri 21 Jan 2028.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2028, 1, 3), new DateTime(2028, 1, 21));

        // Year 4: 8 weeks would be 40 days -- exactly what is left, but more than
        // the 5-week yearly cap allows, so it must fail on the yearly cap.
        var tooMuchForOneYear = PerChildLeaveWorld.Request(child.Id, new DateTime(2029, 1, 1), new DateTime(2029, 2, 23));
        var yearlyError = await PerChildLeaveWorld.CheckAsync(db, tooMuchForOneYear);
        Assert.NotNull(yearlyError);
        Assert.Contains("left for the leave year", yearlyError);

        // 5 weeks in year 4 is within both caps: 40 days remain in total.
        var withinBoth = PerChildLeaveWorld.Request(child.Id, new DateTime(2029, 1, 1), new DateTime(2029, 2, 2));
        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, withinBoth));
    }

    /// <summary>
    /// The lifetime cap, reached without ever breaking the yearly one: 18 weeks
    /// spread over four leave years, then one more day refused.
    /// </summary>
    [Fact]
    public async Task The_eighteen_week_total_is_the_hard_limit()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 25 + 25 + 25 + 15 = 90 business days = 18 weeks.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2027, 1, 4), new DateTime(2027, 2, 5));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2028, 1, 3), new DateTime(2028, 2, 4));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2029, 1, 1), new DateTime(2029, 1, 19));

        var oneMoreDay = PerChildLeaveWorld.Request(child.Id, new DateTime(2030, 1, 7), new DateTime(2030, 1, 7));

        var error = await PerChildLeaveWorld.CheckAsync(db, oneMoreDay);

        Assert.NotNull(error);
        Assert.Contains("remaining in total", error);
        Assert.Contains("0 day(s)", error);
    }

    /// <summary>
    /// Requirement example 2. Three children are three separate ledgers: 5 weeks
    /// each in the same leave year is fine (15 weeks in total), a 6th week for any
    /// one of them is not. The cap is per child, never pooled.
    /// </summary>
    [Fact]
    public async Task Example_two_gives_each_child_their_own_ledger()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var first = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));
        var second = await PerChildLeaveWorld.AddChildAsync(db, "Maria", new DateOnly(2021, 6, 15));
        var third = await PerChildLeaveWorld.AddChildAsync(db, "Petros", new DateOnly(2023, 9, 1));

        // 5 weeks for each child in the same leave year: 15 weeks for the employee.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, first.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, second.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));
        await PerChildLeaveWorld.ApproveLeaveAsync(db, third.Id, new DateTime(2026, 5, 4), new DateTime(2026, 6, 5));

        // A 6th week for the first child in the same year: refused.
        var sixthWeek = PerChildLeaveWorld.Request(first.Id, new DateTime(2026, 9, 7), new DateTime(2026, 9, 11));
        Assert.NotNull(await PerChildLeaveWorld.CheckAsync(db, sixthWeek));

        // The next leave year resets all three.
        var nextYear = PerChildLeaveWorld.Request(first.Id, new DateTime(2027, 1, 4), new DateTime(2027, 1, 8));
        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, nextYear));
    }

    /// <summary>
    /// One child's usage must not touch another's. Without a per-child predicate on
    /// the usage query, three children's leave would total against whichever child
    /// was asked about.
    /// </summary>
    [Fact]
    public async Task One_childs_leave_does_not_charge_another()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var first = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", new DateOnly(2019, 3, 4));
        var second = await PerChildLeaveWorld.AddChildAsync(db, "Maria", new DateOnly(2021, 6, 15));

        await PerChildLeaveWorld.ApproveLeaveAsync(db, first.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        var forSecond = PerChildLeaveWorld.Request(second.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, forSecond));
    }

    /// <summary>
    /// The yearly cap is checked against every leave year the request touches, which
    /// is what stops 10 weeks arriving as one request split over new year.
    /// </summary>
    [Fact]
    public async Task A_request_across_the_year_boundary_is_capped_in_both_years()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 30 Nov 2026 - 05 Feb 2027 is 50 business days: 24 of them fall in the 2026
        // leave year and 26 in 2027. That is 10 weeks in total, well inside the
        // 18-week lifetime cap — but the yearly cap applies to each leave year
        // separately, and the 2027 share of 26 days exceeds its 25-day cap.
        var straddling = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 11, 30), new DateTime(2027, 2, 5));

        var error = await PerChildLeaveWorld.CheckAsync(db, straddling);

        Assert.NotNull(error);
        Assert.Contains("left for the leave year", error);
        // Pins which year broke the cap: the 2027 share (26 days, over the 25-day
        // cap), not the 2026 share (24 days, within it). Without this, an
        // implementation that charged the whole 50-day request against a single
        // leave year would compute 25 < 50, refuse with this same message, and
        // pass just as well — this is what tells the two apart.
        Assert.Contains("01 Jan 2027", error);
    }

    /// <summary>
    /// The complement of the test above: a request that touches two leave years
    /// legitimately fits when each year's own share is within its 25-day cap, even
    /// though the request's total (45 days) exceeds a single year's cap on its own.
    /// Mon 30 Nov 2026 - Fri 29 Jan 2027 splits into 24 business days in the 2026
    /// leave year (30 Nov - 31 Dec, 32 calendar days less 8 weekend days) and 21 in
    /// 2027 (1 - 29 Jan, 29 calendar days less 8 weekend days) — 24 and 21, each
    /// under 25, 45 in total, well under the 90-day lifetime cap with no prior
    /// usage. An implementation that charged the whole request against one leave
    /// year (e.g. by key of the start date) would compute 45 > 25 and wrongly
    /// refuse it — over-refusing a legal request, the more expensive failure
    /// direction than under-refusing.
    /// </summary>
    [Fact]
    public async Task A_legal_straddling_request_is_not_over_refused()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        var straddling = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 11, 30), new DateTime(2027, 1, 29));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, straddling));
    }

    [Fact]
    public async Task Five_weeks_either_side_of_the_boundary_is_allowed()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // Exactly 25 business days ending 31 Dec 2026, then 25 starting 04 Jan 2027.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 11, 27), new DateTime(2026, 12, 31));

        var newYear = PerChildLeaveWorld.Request(child.Id, new DateTime(2027, 1, 4), new DateTime(2027, 2, 5));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, newYear));
    }

    /// <summary>
    /// A leave year that runs April-March moves the reset with it: the same two
    /// requests that were in different years above now fall inside one.
    /// </summary>
    [Fact]
    public async Task The_year_follows_the_configured_leave_year()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync(leaveYearStartMonth: 4);
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 25 business days — the same range the calendar-year test above allows a
        // second 25 alongside, because there the two fall in different leave years.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 11, 27), new DateTime(2026, 12, 31));

        // Both sit inside the Apr 2026 - Mar 2027 leave year, so the second breaks
        // the 5-week cap where under a calendar year it would not.
        var sameLeaveYear = PerChildLeaveWorld.Request(child.Id, new DateTime(2027, 1, 4), new DateTime(2027, 2, 5));

        Assert.NotNull(await PerChildLeaveWorld.CheckAsync(db, sameLeaveYear));
    }

    /// <summary>
    /// Weekends are free. A five-week calendar span is 25 business days, not 35 —
    /// the whole reason a "week" is defined as five business days.
    /// </summary>
    [Fact]
    public async Task Weekends_do_not_consume_entitlement()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // Mon 05 Jan - Fri 06 Feb 2026 is 33 calendar days but exactly 25 business
        // days, so it fits the 5-week yearly cap precisely.
        var exactlyFiveWeeks = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, exactlyFiveWeeks));
    }

    [Fact]
    public async Task Public_holidays_do_not_consume_entitlement()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var settings = await db.AppSettings.FirstAsync();
        settings.HolidayCountryCode = "CY";
        db.PublicHolidays.Add(new PublicHoliday
        {
            CountryCode = "CY",
            Year = 2026,
            Date = new DateTime(2026, 1, 6),
            LocalName = "Epiphany",
            EnglishName = "Epiphany",
        });
        await db.SaveChangesAsync();

        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        // 25 business days already approved minus the holiday leaves one day spare.
        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));

        var oneMoreDay = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 3, 2));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, oneMoreDay));
    }

    [Theory]
    [InlineData(AnnualLeaveStatus.Pending)]
    [InlineData(AnnualLeaveStatus.Rejected)]
    [InlineData(AnnualLeaveStatus.Cancelled)]
    public async Task Only_approved_leave_counts_as_used(AnnualLeaveStatus status)
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = PerChildLeaveWorld.UserId,
            EmployeeProfileId = PerChildLeaveWorld.ProfileId,
            ChildId = child.Id,
            LeaveTypeId = PerChildLeaveWorld.PaternityTypeId,
            StartDate = new DateTime(2026, 1, 5),
            EndDate = new DateTime(2026, 2, 6),
            Reason = "Paternity",
            Status = status,
        });
        await db.SaveChangesAsync();

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }

    /// <summary>
    /// Editing a request must not count that request against itself, or nudging a
    /// date on an approved five-week request would refuse it as a sixth week.
    /// </summary>
    [Fact]
    public async Task An_edited_request_does_not_count_against_itself()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        await PerChildLeaveWorld.ApproveLeaveAsync(db, child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 6));
        var existing = await db.AnnualLeaves.FirstAsync();

        var edited = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 1, 5), new DateTime(2026, 2, 5));
        edited.Id = existing.Id;

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, edited, excludeLeaveId: existing.Id));
    }

    /// <summary>
    /// Paternity rows that predate the per-child entitlement have no child, so they
    /// belong to no ledger. They stay approved and visible, and an admin can attach
    /// a child later by editing them.
    ///
    /// The legacy row and the request are deliberately placed in the SAME leave
    /// year (2026) and each is exactly 25 business days: if the usage query were
    /// not scoped by <c>ChildId</c>, the legacy row's days would count toward the
    /// new child's 2026 yearly cap and this request would be wrongly refused.
    /// Correct code returns null because the legacy row belongs to no child.
    /// </summary>
    [Fact]
    public async Task Legacy_leave_with_no_child_charges_nobody()
    {
        await using var db = await PerChildLeaveWorld.CreateAsync();
        var child = await PerChildLeaveWorld.AddChildAsync(db, "Andreas", YoungChild);

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = PerChildLeaveWorld.UserId,
            EmployeeProfileId = PerChildLeaveWorld.ProfileId,
            ChildId = null,
            LeaveTypeId = PerChildLeaveWorld.PaternityTypeId,
            StartDate = new DateTime(2026, 1, 5),
            EndDate = new DateTime(2026, 2, 6),
            Reason = "Paternity (before children were declared)",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        var request = PerChildLeaveWorld.Request(child.Id, new DateTime(2026, 3, 2), new DateTime(2026, 4, 3));

        Assert.Null(await PerChildLeaveWorld.CheckAsync(db, request));
    }
}
