using Domain.Services;
using Xunit;

namespace WorkTrack.Tests.Domain;

/// <summary>
/// Pure unit tests against the domain service. No DbContext, no reflection, no I/O —
/// the service is stateless and depends on nothing, so the test code looks like the
/// code under test. Anything that needs holiday data passes a HashSet&lt;DateTime&gt; in.
/// </summary>
public class LeaveCalculationServiceTests
{
    // ── CalculateBusinessDays ──────────────────────────────────────────────────

    [Fact]
    public void CalculateBusinessDays_ReturnsZero_WhenEndBeforeStart()
    {
        var days = LeaveCalculationService.CalculateBusinessDays(
            new DateTime(2026, 1, 10), new DateTime(2026, 1, 5));
        Assert.Equal(0, days);
    }

    [Fact]
    public void CalculateBusinessDays_CountsSingleWeekday_AsOne()
    {
        // Mon 2026-01-05 → Mon 2026-01-05 inclusive = 1 business day.
        var days = LeaveCalculationService.CalculateBusinessDays(
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 5));
        Assert.Equal(1, days);
    }

    [Fact]
    public void CalculateBusinessDays_ReturnsZero_ForWeekendOnlyRange()
    {
        // Sat 2026-01-10 → Sun 2026-01-11.
        var days = LeaveCalculationService.CalculateBusinessDays(
            new DateTime(2026, 1, 10), new DateTime(2026, 1, 11));
        Assert.Equal(0, days);
    }

    [Theory]
    [InlineData("2026-01-05", "2026-01-09", 5)] // Mon-Fri
    [InlineData("2026-01-05", "2026-01-11", 5)] // Mon-Sun (weekend trimmed)
    [InlineData("2026-01-04", "2026-01-09", 5)] // Sun-Fri
    [InlineData("2026-01-05", "2026-01-16", 10)] // Two weeks
    public void CalculateBusinessDays_SkipsWeekends(string start, string end, int expected)
    {
        var days = LeaveCalculationService.CalculateBusinessDays(
            DateTime.Parse(start), DateTime.Parse(end));
        Assert.Equal(expected, days);
    }

    [Fact]
    public void CalculateBusinessDays_SkipsHolidaysInRange()
    {
        // Mon-Fri 2026-01-05 → 2026-01-09 = 5 business days; one holiday on Tue 06.
        var holidays = new HashSet<DateTime> { new(2026, 1, 6) };
        var days = LeaveCalculationService.CalculateBusinessDays(
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 9), holidays);
        Assert.Equal(4, days);
    }

    [Fact]
    public void CalculateBusinessDays_DoesNotDoubleSkipHolidayOnWeekend()
    {
        // Saturday 2026-01-10 is in the holiday set AND a weekend. The weekend filter
        // already excludes it; the holiday filter must not deduct another day.
        var holidays = new HashSet<DateTime> { new(2026, 1, 10) };
        var days = LeaveCalculationService.CalculateBusinessDays(
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 16), holidays);
        Assert.Equal(10, days);
    }

    [Fact]
    public void CalculateBusinessDays_TreatsNullAndEmptyHolidaySetIdentically()
    {
        var withNull = LeaveCalculationService.CalculateBusinessDays(
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 9), holidays: null);
        var withEmpty = LeaveCalculationService.CalculateBusinessDays(
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 9), holidays: new HashSet<DateTime>());
        Assert.Equal(withNull, withEmpty);
    }

    [Fact]
    public void CalculateBusinessDays_NormalizesTimeOfDay()
    {
        // Time component must be ignored — 9am Mon to 6pm Mon is still one business day.
        var days = LeaveCalculationService.CalculateBusinessDays(
            new DateTime(2026, 1, 5, 9, 0, 0), new DateTime(2026, 1, 5, 18, 0, 0));
        Assert.Equal(1, days);
    }

    // ── CalculateChargeableDays ────────────────────────────────────────────────

    [Fact]
    public void CalculateChargeableDays_Full_EqualsBusinessDays()
    {
        var days = LeaveCalculationService.CalculateChargeableDays(
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 9), LeaveDuration.Full);
        Assert.Equal(5m, days);
    }

    [Fact]
    public void CalculateChargeableDays_HalfDay_IsHalfOfBusinessDays()
    {
        var days = LeaveCalculationService.CalculateChargeableDays(
            new DateTime(2026, 1, 5), new DateTime(2026, 1, 5), LeaveDuration.HalfDay);
        Assert.Equal(0.5m, days);
    }

    [Fact]
    public void CalculateChargeableDays_HalfDay_ReturnsZero_WhenNoBusinessDays()
    {
        // Saturday only — no business days → no charge even at half rate.
        var days = LeaveCalculationService.CalculateChargeableDays(
            new DateTime(2026, 1, 10), new DateTime(2026, 1, 10), LeaveDuration.HalfDay);
        Assert.Equal(0m, days);
    }

    // ── GetLeaveYearKey ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-01-15", 1, 2026)] // Jan within Jan-start year
    [InlineData("2026-12-31", 1, 2026)] // Dec within Jan-start year
    [InlineData("2026-04-01", 4, 2026)] // First day of April-start year
    [InlineData("2026-03-31", 4, 2025)] // Day before April-start year → previous key
    [InlineData("2026-02-15", 4, 2025)] // Feb belongs to prior April-start year
    [InlineData("2026-05-01", 4, 2026)] // May after April → current year
    public void GetLeaveYearKey_HonorsStartMonth(string isoDate, int startMonth, int expected)
    {
        var key = LeaveCalculationService.GetLeaveYearKey(DateTime.Parse(isoDate), startMonth);
        Assert.Equal(expected, key);
    }

    // ── GetLeaveYearBounds ─────────────────────────────────────────────────────

    [Fact]
    public void GetLeaveYearBounds_CalendarYear_IsJanToDec()
    {
        var (start, end) = LeaveCalculationService.GetLeaveYearBounds(2026, 1);
        Assert.Equal(new DateTime(2026, 1, 1), start);
        Assert.Equal(new DateTime(2026, 12, 31), end);
    }

    [Fact]
    public void GetLeaveYearBounds_AprilStart_IsAprToMar()
    {
        var (start, end) = LeaveCalculationService.GetLeaveYearBounds(2025, 4);
        Assert.Equal(new DateTime(2025, 4, 1), start);
        Assert.Equal(new DateTime(2026, 3, 31), end);
    }

    // ── GetCoveredLeaveYears ───────────────────────────────────────────────────

    [Fact]
    public void GetCoveredLeaveYears_WithinSingleYear_ReturnsOneKey()
    {
        var keys = LeaveCalculationService.GetCoveredLeaveYears(
            new DateTime(2026, 2, 1), new DateTime(2026, 2, 5), startMonth: 1).ToList();
        Assert.Equal(new[] { 2026 }, keys);
    }

    [Fact]
    public void GetCoveredLeaveYears_AcrossYearBoundary_ReturnsBoth()
    {
        // Calendar year boundary: Dec 2025 → Jan 2026 with startMonth=1.
        var keys = LeaveCalculationService.GetCoveredLeaveYears(
            new DateTime(2025, 12, 28), new DateTime(2026, 1, 5), startMonth: 1).ToList();
        Assert.Equal(new[] { 2025, 2026 }, keys);
    }

    [Fact]
    public void GetCoveredLeaveYears_AcrossAprilStartBoundary_ReturnsBoth()
    {
        // March 2026 → April 2026 with startMonth=4 crosses leave-year boundary.
        var keys = LeaveCalculationService.GetCoveredLeaveYears(
            new DateTime(2026, 3, 30), new DateTime(2026, 4, 5), startMonth: 4).ToList();
        Assert.Equal(new[] { 2025, 2026 }, keys);
    }

    // ── CalculateBusinessDaysInLeaveYear ───────────────────────────────────────

    [Fact]
    public void CalculateBusinessDaysInLeaveYear_ReturnsZero_ForLeaveOutsideYear()
    {
        // Leave in Feb 2026 vs leave-year key 2025 (Apr 2025 – Mar 2026): inside.
        // Leave in Feb 2027 vs leave-year key 2025: outside → 0.
        var days = LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
            leaveStart: new DateTime(2027, 2, 2),
            leaveEnd: new DateTime(2027, 2, 6),
            leaveYearKey: 2025,
            startMonth: 4);
        Assert.Equal(0, days);
    }

    [Fact]
    public void CalculateBusinessDaysInLeaveYear_ClipsToYearBounds()
    {
        // Leave spans Mar 30 – Apr 3, 2026. With April-start leave year, the 2025 key
        // covers Apr 2025 – Mar 2026, so only Mar 30 and Mar 31 (both weekdays) count.
        // 2026-03-30 is Monday, 2026-03-31 is Tuesday → 2 business days in LY 2025.
        var days = LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
            leaveStart: new DateTime(2026, 3, 30),
            leaveEnd: new DateTime(2026, 4, 3),
            leaveYearKey: 2025,
            startMonth: 4);
        Assert.Equal(2, days);
    }

    [Fact]
    public void CalculateBusinessDaysInLeaveYear_RespectsHolidays()
    {
        var holidays = new HashSet<DateTime> { new(2026, 1, 6) };
        var days = LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
            leaveStart: new DateTime(2026, 1, 5),
            leaveEnd: new DateTime(2026, 1, 9),
            leaveYearKey: 2026,
            startMonth: 1,
            holidays: holidays);
        Assert.Equal(4, days);
    }

    // ── CalculateRemainingBalance ──────────────────────────────────────────────

    [Theory]
    [InlineData(20, 5, 15)]
    [InlineData(20, 20, 0)]
    [InlineData(20, 25, 0)] // floored at zero on overdraft
    [InlineData(0, 0, 0)]
    public void CalculateRemainingBalance_FloorsAtZero(int entitlement, int used, int expected)
    {
        Assert.Equal(expected, LeaveCalculationService.CalculateRemainingBalance(entitlement, used));
    }
}
