using Application.Timesheets;
using Domain;
using Xunit;

namespace WorkTrack.Tests.Application;

public class TimesheetEntryValidatorTests
{
    private static TimesheetEntry Entry(
        int projectId,
        decimal hours,
        DateTime? date = null,
        string? id = null,
        string timesheetId = "ts-1") => new()
    {
        Id = id ?? Guid.NewGuid().ToString(),
        TimesheetId = timesheetId,
        ProjectId = projectId,
        Date = date ?? new DateTime(2026, 5, 4), // arbitrary Monday
        HoursWorked = hours,
    };

    // ── basic invariants ────────────────────────────────────────────────────

    [Fact]
    public void Rejects_NonPositive_Hours()
    {
        var candidate = Entry(projectId: 1, hours: 0);
        var result = TimesheetEntryValidator.Validate(candidate, existing: []);
        Assert.False(result.IsValid);
        Assert.Contains("greater than zero", result.Error);
    }

    [Fact]
    public void Rejects_Negative_Hours()
    {
        var candidate = Entry(projectId: 1, hours: -2);
        var result = TimesheetEntryValidator.Validate(candidate, existing: []);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_Single_Entry_Above_24_Hours()
    {
        var candidate = Entry(projectId: 1, hours: 25);
        var result = TimesheetEntryValidator.Validate(candidate, existing: []);
        Assert.False(result.IsValid);
        Assert.Contains("cannot exceed", result.Error);
    }

    [Fact]
    public void Accepts_Single_Entry_At_Exactly_24_Hours()
    {
        // Boundary: 24h is the cap, not "less than 24". Treat as inclusive.
        var candidate = Entry(projectId: 1, hours: 24);
        var result = TimesheetEntryValidator.Validate(candidate, existing: []);
        Assert.True(result.IsValid);
    }

    // ── overlap (same project on same day) ──────────────────────────────────

    [Fact]
    public void Rejects_When_Same_Project_Already_Exists_On_Same_Day()
    {
        var existing = new[] { Entry(projectId: 1, hours: 3, id: "existing") };
        var candidate = Entry(projectId: 1, hours: 2); // same date by default, same project
        var result = TimesheetEntryValidator.Validate(candidate, existing);
        Assert.False(result.IsValid);
        Assert.Contains("already exists", result.Error);
    }

    [Fact]
    public void Accepts_When_Same_Project_On_Different_Day()
    {
        var existing = new[] { Entry(projectId: 1, hours: 3, date: new DateTime(2026, 5, 4)) };
        var candidate = Entry(projectId: 1, hours: 2, date: new DateTime(2026, 5, 5));
        var result = TimesheetEntryValidator.Validate(candidate, existing);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Accepts_When_Different_Project_On_Same_Day()
    {
        var existing = new[] { Entry(projectId: 1, hours: 3) };
        var candidate = Entry(projectId: 2, hours: 4); // same date, different project
        var result = TimesheetEntryValidator.Validate(candidate, existing);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_To_Self_Is_Not_Treated_As_Overlap()
    {
        // When updating, the existing list contains the entry being edited.
        // Validator must filter by Id to avoid a false-positive overlap with itself.
        var sameId = "entry-being-edited";
        var existing = new[] { Entry(projectId: 1, hours: 8, id: sameId) };
        var candidate = Entry(projectId: 1, hours: 4, id: sameId);
        var result = TimesheetEntryValidator.Validate(candidate, existing);
        Assert.True(result.IsValid);
    }

    // ── 24-hour daily cap across multiple entries ───────────────────────────

    [Fact]
    public void Rejects_When_Same_Day_Total_Would_Exceed_24_Hours()
    {
        var existing = new[]
        {
            Entry(projectId: 1, hours: 10),
            Entry(projectId: 2, hours: 10),
        };
        var candidate = Entry(projectId: 3, hours: 5); // 10 + 10 + 5 = 25 > 24
        var result = TimesheetEntryValidator.Validate(candidate, existing);
        Assert.False(result.IsValid);
        Assert.Contains("exceeds", result.Error);
    }

    [Fact]
    public void Accepts_When_Same_Day_Total_Equals_Exactly_24_Hours()
    {
        var existing = new[]
        {
            Entry(projectId: 1, hours: 12),
            Entry(projectId: 2, hours: 8),
        };
        var candidate = Entry(projectId: 3, hours: 4); // 12 + 8 + 4 = 24
        var result = TimesheetEntryValidator.Validate(candidate, existing);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void DailyCap_Does_Not_Count_Other_Days()
    {
        var existing = new[]
        {
            Entry(projectId: 1, hours: 20, date: new DateTime(2026, 5, 4)),
            Entry(projectId: 1, hours: 20, date: new DateTime(2026, 5, 5)),
        };
        // New 5-hour entry on a third day — same-day total is just 5, well under 24.
        var candidate = Entry(projectId: 1, hours: 5, date: new DateTime(2026, 5, 6));
        var result = TimesheetEntryValidator.Validate(candidate, existing);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void DailyCap_Ignores_Time_Of_Day()
    {
        // Same calendar day, different times — should still be counted together.
        var existing = new[] { Entry(projectId: 1, hours: 22, date: new DateTime(2026, 5, 4, 9, 0, 0)) };
        var candidate = Entry(projectId: 2, hours: 3, date: new DateTime(2026, 5, 4, 18, 0, 0));
        var result = TimesheetEntryValidator.Validate(candidate, existing);
        Assert.False(result.IsValid);
        Assert.Contains("exceeds", result.Error);
    }

    // ── future-date lock ────────────────────────────────────────────────────

    [Fact]
    public void Rejects_Entry_For_Future_Date()
    {
        var today = new DateTime(2026, 5, 26);
        var candidate = Entry(projectId: 1, hours: 4, date: new DateTime(2026, 5, 27));
        var result = TimesheetEntryValidator.Validate(candidate, existing: [], today: today);
        Assert.False(result.IsValid);
        Assert.Contains("future", result.Error);
    }

    [Fact]
    public void Accepts_Entry_For_Today()
    {
        var today = new DateTime(2026, 5, 26);
        var candidate = Entry(projectId: 1, hours: 4, date: new DateTime(2026, 5, 26));
        var result = TimesheetEntryValidator.Validate(candidate, existing: [], today: today);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Accepts_Entry_For_Past_Date()
    {
        var today = new DateTime(2026, 5, 26);
        var candidate = Entry(projectId: 1, hours: 4, date: new DateTime(2026, 5, 20));
        var result = TimesheetEntryValidator.Validate(candidate, existing: [], today: today);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Future_Check_Ignores_Time_Of_Day()
    {
        // Same calendar day at 23:59 should still be "today", not future.
        var today = new DateTime(2026, 5, 26, 10, 0, 0);
        var candidate = Entry(projectId: 1, hours: 4, date: new DateTime(2026, 5, 26, 23, 59, 0));
        var result = TimesheetEntryValidator.Validate(candidate, existing: [], today: today);
        Assert.True(result.IsValid);
    }
}
