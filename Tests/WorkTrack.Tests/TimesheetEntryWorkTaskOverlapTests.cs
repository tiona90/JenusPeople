using Application.Timesheets.Support;
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Two rows on the same project, type and component on one date are a duplicate —
/// unless they point at different tasks. Two pieces of work on one project are two
/// facts, and Phase 2 made the "no type, no component" row the common case, so
/// without this a second task on the same project could not be logged that day.
/// </summary>
public class TimesheetEntryWorkTaskOverlapTests
{
    private static readonly DateTime Day = new(2026, 10, 19);
    private static readonly DateTime Today = new(2026, 10, 20);

    private static TimesheetEntry Row(string id, int? taskId, decimal hours = 2m) => new()
    {
        Id = id, TimesheetId = "ts", ProjectId = 10, Date = Day, HoursWorked = hours, WorkTaskId = taskId,
    };

    [Fact]
    public void Two_tasks_on_the_same_project_and_day_are_two_entries()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: 2), [Row("a", taskId: 1)], Today);
        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void A_task_row_beside_a_row_with_no_task_is_not_a_duplicate()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: 1), [Row("a", taskId: null)], Today);
        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void The_same_task_twice_on_one_day_is_still_a_duplicate()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: 1), [Row("a", taskId: 1)], Today);
        Assert.False(result.IsValid);
        Assert.Contains("already exists", result.Error);
    }

    [Fact]
    public void Two_rows_with_no_task_still_collide_as_before()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: null), [Row("a", taskId: null)], Today);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Different_tasks_still_share_the_24_hour_day()
    {
        var result = TimesheetEntryValidator.Validate(Row("b", taskId: 2, hours: 17m), [Row("a", taskId: 1, hours: 8m)], Today);
        Assert.False(result.IsValid);
        Assert.Contains("daily cap", result.Error);
    }
}
