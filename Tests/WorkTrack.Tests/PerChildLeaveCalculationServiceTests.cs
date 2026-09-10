using Domain.Services;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Paternity leave is stated in weeks, but every other leave calculation in the app
/// counts business days. This service is the only place the two meet, and the only
/// place a date of birth becomes an age — nothing stores a child's age, so that a
/// child ages out of eligibility with no job to run and no column to go stale.
/// </summary>
public class PerChildLeaveCalculationServiceTests
{
    [Theory]
    [InlineData(18, 90)]
    [InlineData(5, 25)]
    [InlineData(1, 5)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void A_week_is_five_business_days(int weeks, int expectedDays)
    {
        Assert.Equal(expectedDays, PerChildLeaveCalculationService.WeeksToBusinessDays(weeks));
    }

    [Theory]
    [InlineData(90, 18.0)]
    [InlineData(25, 5.0)]
    [InlineData(6, 1.2)]
    [InlineData(0, 0.0)]
    public void Days_read_back_as_weeks_for_display(int days, decimal expectedWeeks)
    {
        Assert.Equal(expectedWeeks, PerChildLeaveCalculationService.BusinessDaysToWeeks(days));
    }

    [Fact]
    public void Age_turns_over_on_the_birthday_not_before_it()
    {
        var dob = new DateOnly(2011, 3, 4);

        Assert.Equal(14, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2026, 3, 3)));
        Assert.Equal(15, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2026, 3, 4)));
        Assert.Equal(15, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2026, 3, 5)));
    }

    /// <summary>
    /// A 29 February child completes a year of life on 1 March in a common year.
    /// Treating 28 February as the birthday would hand them a day of entitlement
    /// they are no longer entitled to.
    /// </summary>
    [Fact]
    public void A_leap_day_child_ages_on_the_first_of_March_in_a_common_year()
    {
        var dob = new DateOnly(2012, 2, 29);

        Assert.Equal(14, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2027, 2, 28)));
        Assert.Equal(15, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2027, 3, 1)));
        // 2028 is a leap year, so the birthday is the real one.
        Assert.Equal(15, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2028, 2, 28)));
        Assert.Equal(16, PerChildLeaveCalculationService.AgeOn(dob, new DateOnly(2028, 2, 29)));
    }

    [Fact]
    public void The_last_eligible_date_is_the_day_before_the_fifteenth_birthday()
    {
        Assert.Equal(
            new DateOnly(2026, 3, 3),
            PerChildLeaveCalculationService.LastEligibleDate(new DateOnly(2011, 3, 4), maxAge: 15));

        Assert.Equal(
            new DateOnly(2027, 2, 28),
            PerChildLeaveCalculationService.LastEligibleDate(new DateOnly(2012, 2, 29), maxAge: 15));
    }

    [Fact]
    public void Eligibility_ends_on_the_fifteenth_birthday()
    {
        var dob = new DateOnly(2011, 3, 4);

        Assert.True(PerChildLeaveCalculationService.IsEligibleOn(dob, new DateOnly(2026, 3, 3), 15));
        Assert.False(PerChildLeaveCalculationService.IsEligibleOn(dob, new DateOnly(2026, 3, 4), 15));
    }

    /// <summary>
    /// Floored at zero, mirroring LeaveCalculationService.CalculateRemainingBalance:
    /// a negative remaining figure is meaningless on a screen and dangerous in a
    /// comparison.
    /// </summary>
    [Theory]
    [InlineData(90, 25, 65)]
    [InlineData(90, 90, 0)]
    [InlineData(90, 120, 0)]
    public void Remaining_never_goes_negative(int total, int used, int expected)
    {
        Assert.Equal(expected, PerChildLeaveCalculationService.RemainingDays(total, used));
    }
}
