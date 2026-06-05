using System;

namespace Domain;

public class TimesheetEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TimesheetId { get; set; } = string.Empty;
    public Timesheet? Timesheet { get; set; }
    public int ProjectId { get; set; }
    public Project? Project { get; set; }
    public DateTime Date { get; set; }
    public decimal HoursWorked { get; set; }
    public string? Notes { get; set; }

    // Optional activity category for the work logged (Development, Testing, …).
    // Nullable so existing entries and the minimal project+hours flow stay valid.
    public int? ActivityTypeId { get; set; }
    public ProjectActivityType? ActivityType { get; set; }
}
