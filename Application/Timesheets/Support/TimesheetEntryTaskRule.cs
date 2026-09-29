using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Support;

/// <summary>
/// Which task a timesheet row may be logged against: one its <b>owner</b> is
/// assigned to (not the caller — an HR Administrator writes on somebody's behalf
/// and is never an assignee), still open, on the row's own project.
///
/// Asked only when the row's task or project changes. A row that keeps both is
/// never re-checked, so closing a task or taking somebody off it leaves the hours
/// already logged where they are — the same "check what changed" shape as
/// WorkTaskAssigneeRule.
/// </summary>
public static class TimesheetEntryTaskRule
{
    /// <summary>Also the answer for a task that does not exist, so an id cannot be probed.</summary>
    public const string NotYoursMessage = "That task is not one of yours.";
    public const string ClosedMessage = "That task is closed.";
    public const string OtherProjectMessage = "That task belongs to another project.";

    /// <param name="stored">The row as saved, or null when <paramref name="candidate"/> is new.</param>
    /// <returns>Null when the row may be saved, otherwise the refusal.</returns>
    public static async Task<string?> CheckAsync(
        AppDbContext context,
        string timesheetId,
        TimesheetEntry candidate,
        TimesheetEntry? stored,
        CancellationToken cancellationToken)
    {
        if (candidate.WorkTaskId is not { } taskId) return null;
        if (stored is not null && stored.WorkTaskId == taskId && stored.ProjectId == candidate.ProjectId) return null;

        var ownerUserId = await context.Timesheets
            .Where(t => t.Id == timesheetId)
            .Join(context.EmployeeProfiles.IgnoreQueryFilters(), t => t.EmployeeProfileId, p => p.Id, (t, p) => p.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        var task = await context.WorkTasks
            .AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.Status, t.ProjectId, Assigned = t.Assignees.Any(a => a.UserId == ownerUserId) })
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null || !task.Assigned) return NotYoursMessage;
        // Waiting for confirmation is still open: hours from that week may be logged after the Done.
        if (task.Status is not (WorkTaskStatus.ToDo or WorkTaskStatus.InProgress or WorkTaskStatus.AwaitingConfirmation)) return ClosedMessage;
        if (task.ProjectId != candidate.ProjectId) return OtherProjectMessage;
        return null;
    }
}
