using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// (3) AnnualLeave.TotalDays — weekend exclusion. Pure computed property over
/// LeaveCalculationService.CalculateBusinessDays. 2024-01-01 is a Monday, which
/// anchors every date below.
/// </summary>
public class AnnualLeaveTotalDaysTests
{
    private static int TotalDays(string start, string end) =>
        new AnnualLeave { StartDate = DateTime.Parse(start), EndDate = DateTime.Parse(end) }.TotalDays;

    [Theory]
    // Mon..Fri inclusive = 5 working days
    [InlineData("2024-01-01", "2024-01-05", 5)]
    // Mon..Sun inclusive = 5 (Sat 6th + Sun 7th excluded)
    [InlineData("2024-01-01", "2024-01-07", 5)]
    // Two full working weeks Mon..Fri = 10
    [InlineData("2024-01-01", "2024-01-12", 10)]
    // A single weekday = 1
    [InlineData("2024-01-02", "2024-01-02", 1)]
    public void Counts_only_weekdays(string start, string end, int expected)
    {
        Assert.Equal(expected, TotalDays(start, end));
    }

    [Fact]
    public void Weekend_only_range_is_zero()
    {
        // Sat 2024-01-06 .. Sun 2024-01-07
        Assert.Equal(0, TotalDays("2024-01-06", "2024-01-07"));
    }

    [Fact]
    public void Single_saturday_is_zero()
    {
        Assert.Equal(0, TotalDays("2024-01-06", "2024-01-06"));
    }

    [Fact]
    public void End_before_start_is_zero()
    {
        Assert.Equal(0, TotalDays("2024-01-05", "2024-01-01"));
    }

    [Fact]
    public void Time_component_is_ignored()
    {
        var leave = new AnnualLeave
        {
            StartDate = new DateTime(2024, 1, 1, 23, 59, 0),
            EndDate = new DateTime(2024, 1, 5, 0, 1, 0),
        };
        Assert.Equal(5, leave.TotalDays);
    }
}
