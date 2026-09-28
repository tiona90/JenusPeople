using Application.Core;
using Application.Timesheets.Support;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Queries;

/// <summary>
/// The tasks a timesheet's rows may be logged against: every open task its owner
/// is assigned to, plus any task a row on the sheet already names (so an old row
/// never opens on a blank picker), the latter flagged closed when it is.
///
/// With no <see cref="Query.TimesheetId"/> the owner is the caller — a week with
/// no sheet yet. With one, the sheet must be inside the caller's timesheet scope,
/// or it is "not found", exactly as GetTimesheetDetail answers.
/// </summary>
public class GetTimesheetTaskOptions
{
    public class Query : IRequest<Result<List<TimesheetTaskOptionDto>>>
    {
        public string CallerUserId { get; set; } = string.Empty;
        public string? TimesheetId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsHrAdministrator { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<TimesheetTaskOptionDto>>>
    {
        public async Task<Result<List<TimesheetTaskOptionDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var ownerUserId = request.CallerUserId;
            List<int> onSheet = [];

            if (!string.IsNullOrEmpty(request.TimesheetId))
            {
                var scoped = await TimesheetScope.ApplyAsync(
                    context,
                    context.Timesheets.AsNoTracking().Where(t => t.Id == request.TimesheetId),
                    request.CallerUserId,
                    request.IsAdmin,
                    request.IsManager,
                    cancellationToken,
                    isHrAdministrator: request.IsHrAdministrator);

                var sheet = await scoped
                    .Join(context.EmployeeProfiles.IgnoreQueryFilters(), t => t.EmployeeProfileId, p => p.Id,
                        (t, p) => new { t.Id, p.UserId })
                    .FirstOrDefaultAsync(cancellationToken);
                if (sheet is null) return Result<List<TimesheetTaskOptionDto>>.Failure("Timesheet not found.");

                ownerUserId = sheet.UserId;
                onSheet = await context.TimesheetEntries
                    .Where(e => e.TimesheetId == sheet.Id && e.WorkTaskId != null)
                    .Select(e => e.WorkTaskId!.Value)
                    .Distinct()
                    .ToListAsync(cancellationToken);
            }

            // Somebody reading another person's sheet (a reviewer) gets only what the
            // sheet names. The sheet was admitted by the department it was filed under,
            // which may no longer be the owner's, so the owner's other tasks may sit in
            // a department outside the reader's task scope.
            var readingOwnSheet = ownerUserId == request.CallerUserId;

            var options = await context.WorkTasks
                .AsNoTracking()
                .Where(t => onSheet.Contains(t.Id)
                    || (readingOwnSheet
                        && (t.Status == WorkTaskStatus.ToDo || t.Status == WorkTaskStatus.InProgress)
                        && t.Assignees.Any(a => a.UserId == ownerUserId)))
                .OrderBy(t => t.Title)
                .Select(t => new TimesheetTaskOptionDto
                {
                    Id = t.Id,
                    Title = t.Title,
                    ProjectId = t.ProjectId,
                    ProjectCode = t.Project != null ? t.Project.Code : null,
                    TargetHours = t.TargetHours,
                    IsClosed = t.Status == WorkTaskStatus.Done || t.Status == WorkTaskStatus.Cancelled,
                    IsAssigned = t.Assignees.Any(a => a.UserId == ownerUserId),
                })
                .ToListAsync(cancellationToken);

            var logged = await WorkTaskProjection.LoggedHoursAsync(context, options.Select(o => o.Id).ToList(), cancellationToken);
            foreach (var option in options) option.LoggedHours = logged.GetValueOrDefault(option.Id);

            return Result<List<TimesheetTaskOptionDto>>.Success(options);
        }
    }
}
