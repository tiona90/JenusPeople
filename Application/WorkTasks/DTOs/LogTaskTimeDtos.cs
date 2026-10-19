namespace Application.WorkTasks.DTOs;

/// <summary>Hours an assignee logs on a task from its card.</summary>
public class LogTaskTimeRequest
{
    public DateOnly Date { get; set; }
    public decimal Hours { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Where the hours went, so the card can say so and link to the week.</summary>
public class LogTaskTimeResult
{
    /// <summary>The task as it now reads — its LoggedHours includes the new hours.</summary>
    public WorkTaskDto Task { get; set; } = new();
    public string TimesheetId { get; set; } = string.Empty;
    public string EntryId { get; set; } = string.Empty;
    /// <summary>The Monday the week starts on.</summary>
    public DateOnly WeekStart { get; set; }
    /// <summary>The task's hours on that date, after this log.</summary>
    public decimal HoursOnEntry { get; set; }
    /// <summary>Everything on the sheet for that date, after this log.</summary>
    public decimal HoursOnDay { get; set; }
    public string TimesheetStatus { get; set; } = string.Empty;
}
