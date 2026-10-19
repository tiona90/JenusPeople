using Application.Core;
using Application.Timesheets.Support;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Commands;

/// <summary>
/// An assignee logs hours on an open task without opening the timesheet editor.
/// The week's sheet is found or made (Draft), the task's entry for that date is
/// added to or created, and every rule the editor's own save meets still applies —
/// <see cref="TimesheetEntryValidator"/> and <see cref="TimesheetEntryTaskRule"/>
/// run exactly as <c>TimesheetEntriesController.AddEntry</c> runs them. Emails nobody.
/// </summary>
public class LogTaskTime
{
    public const string NotAnAssigneeMessage = "Only someone working on this task can log time on it.";
    public const string TaskClosedMessage = "This task is closed. Reopen it to log more time.";
    public const string TaskHasNoProjectMessage = "This task has no project, so its hours have nowhere to go on a timesheet.";
    public const string WeekSubmittedMessage = "This week's timesheet has already been submitted. Open it from Timesheets to change it.";
    public const string HoursMessage = "Hours must be in half hours, between 0.5 and 24.";
    public const string FutureDateMessage = "Time can't be logged for a future date.";
    public const string NoProfileMessage = "No employee profile found for the current user.";

    public class Command : IRequest<Result<LogTaskTimeResult>>
    {
        public int TaskId { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        /// <summary>An Employee: only a task they are on or created is visible.</summary>
        public bool AssignedOnly { get; set; }
        public DateOnly Date { get; set; }
        public decimal Hours { get; set; }
        public string? Notes { get; set; }
        /// <summary>Test seam; the controller leaves it null.</summary>
        public DateTime? NowUtc { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<LogTaskTimeResult>>
    {
        public async Task<Result<LogTaskTimeResult>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.TaskId, request.CallerUserId, cancellationToken, request.AssignedOnly);
            if (task is null)
                return Result<LogTaskTimeResult>.Failure(WorkTaskAccess.NotFoundMessage);
            if (!task.Assignees.Any(a => a.UserId == request.CallerUserId))
                return Result<LogTaskTimeResult>.Forbidden(NotAnAssigneeMessage);
            if (task.Status is not (WorkTaskStatus.ToDo or WorkTaskStatus.InProgress or WorkTaskStatus.AwaitingConfirmation))
                return Result<LogTaskTimeResult>.Conflict(TaskClosedMessage);
            if (task.ProjectId is not { } projectId)
                return Result<LogTaskTimeResult>.Conflict(TaskHasNoProjectMessage);

            if (request.Hours < 0.5m || request.Hours > TimesheetEntryValidator.MaxHoursPerDay || request.Hours * 2 != decimal.Truncate(request.Hours * 2))
                return Result<LogTaskTimeResult>.Invalid(HoursMessage);
            var now = request.NowUtc ?? DateTime.UtcNow;
            // The same "future" TimesheetEntryValidator applies: the UTC calendar day.
            if (request.Date > DateOnly.FromDateTime(now))
                return Result<LogTaskTimeResult>.Invalid(FutureDateMessage);

            var profile = await context.EmployeeProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == request.CallerUserId, cancellationToken);
            if (profile is null)
                return Result<LogTaskTimeResult>.Failure(NoProfileMessage);

            // Weeks run Monday to Sunday; a Sunday belongs to the Monday before it.
            var weekStart = request.Date.AddDays(-(((int)request.Date.DayOfWeek + 6) % 7));
            var periodStart = weekStart.ToDateTime(TimeOnly.MinValue);
            var day = request.Date.ToDateTime(TimeOnly.MinValue);

            var sheet = await context.Timesheets
                .FirstOrDefaultAsync(t => t.EmployeeProfileId == profile.Id && t.PeriodStart == periodStart, cancellationToken);
            if (sheet is not null && sheet.Status is not (TimesheetStatus.Draft or TimesheetStatus.Rejected))
                return Result<LogTaskTimeResult>.Conflict(WeekSubmittedMessage);

            // Nothing is added to the context until every rule has passed, so a
            // refusal leaves no half-made sheet behind.
            var isNewSheet = sheet is null;
            sheet ??= new Timesheet
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeProfileId = profile.Id,
                DepartmentId = profile.DepartmentId,
                PeriodStart = periodStart,
                PeriodEnd = weekStart.AddDays(6).ToDateTime(TimeOnly.MinValue),
                Status = TimesheetStatus.Draft,
                CreatedAt = now,
            };

            var existing = isNewSheet
                ? []
                : await context.TimesheetEntries.Where(e => e.TimesheetId == sheet.Id).ToListAsync(cancellationToken);
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
            var entry = existing.FirstOrDefault(e => e.WorkTaskId == task.Id && e.Date.Date == day);

            TimesheetEntry candidate;
            if (entry is not null)
            {
                candidate = new TimesheetEntry
                {
                    Id = entry.Id, TimesheetId = entry.TimesheetId, ProjectId = entry.ProjectId, Date = entry.Date,
                    HoursWorked = entry.HoursWorked + request.Hours,
                    Notes = notes is null ? entry.Notes : string.IsNullOrEmpty(entry.Notes) ? notes : $"{entry.Notes}\n{notes}",
                    ProjectTypeId = entry.ProjectTypeId, ProjectComponentId = entry.ProjectComponentId,
                    ActivityTypeId = entry.ActivityTypeId, WorkTaskId = entry.WorkTaskId,
                };
            }
            else
            {
                var (typeId, componentId, activityId) = await TimesheetEntryDefaults.SingleOptionsAsync(context, projectId, cancellationToken);
                candidate = new TimesheetEntry
                {
                    Id = Guid.NewGuid().ToString(), TimesheetId = sheet.Id, ProjectId = projectId, Date = day,
                    HoursWorked = request.Hours, Notes = notes, WorkTaskId = task.Id,
                    ProjectTypeId = typeId, ProjectComponentId = componentId, ActivityTypeId = activityId,
                };
            }

            var validation = TimesheetEntryValidator.Validate(
                candidate, existing, now,
                assignedActivityTypeIds: await context.ProjectActivityAssignments.Where(a => a.ProjectId == projectId).Select(a => a.ActivityTypeId).ToListAsync(cancellationToken),
                assignedProjectTypeIds: await context.ProjectTypeAssignments.Where(a => a.ProjectId == projectId).Select(a => a.ProjectTypeId).ToListAsync(cancellationToken),
                assignedComponentIds: await context.ProjectComponentAssignments.Where(a => a.ProjectId == projectId).Select(a => a.ComponentId).ToListAsync(cancellationToken));
            if (!validation.IsValid)
                return Result<LogTaskTimeResult>.Invalid(validation.Error!);

            // A new sheet is not saved yet, so the rule cannot find its owner through it;
            // the caller is the owner, and FindVisibleAsync plus the assignee check above
            // already established what the rule asks. An existing sheet is asked as usual.
            if (!isNewSheet)
            {
                var refusal = await TimesheetEntryTaskRule.CheckAsync(context, sheet.Id, candidate, entry, cancellationToken);
                if (refusal is not null)
                    return Result<LogTaskTimeResult>.Invalid(refusal);
            }

            if (isNewSheet) context.Timesheets.Add(sheet);
            if (entry is not null)
            {
                entry.HoursWorked = candidate.HoursWorked;
                entry.Notes = candidate.Notes;
            }
            else
            {
                context.TimesheetEntries.Add(candidate);
            }
            var others = existing.Where(e => e.Id != candidate.Id).Sum(e => e.HoursWorked);
            sheet.TotalHours = others + candidate.HoursWorked;

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (isNewSheet)
            {
                // Two logs for an unsaved week raced: the unique index on
                // (EmployeeProfileId, PeriodStart) let one through. Ask again.
                return Result<LogTaskTimeResult>.Conflict("Your timesheet for this week was just created. Please try again.");
            }

            var hoursOnDay = existing.Where(e => e.Id != candidate.Id && e.Date.Date == day).Sum(e => e.HoursWorked) + candidate.HoursWorked;
            return Result<LogTaskTimeResult>.Success(new LogTaskTimeResult
            {
                Task = await WorkTaskProjection.LoadDtoAsync(context, task.Id, request.CallerUserId, cancellationToken, callerManages: !request.AssignedOnly),
                TimesheetId = sheet.Id,
                EntryId = candidate.Id,
                WeekStart = weekStart,
                HoursOnEntry = candidate.HoursWorked,
                HoursOnDay = hoursOnDay,
                TimesheetStatus = sheet.Status.ToString(),
            });
        }
    }
}
