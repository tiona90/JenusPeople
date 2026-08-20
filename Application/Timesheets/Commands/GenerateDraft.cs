using Application.Core;
using Application.Timesheets.DTOs;
using Application.Timesheets.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Commands;

public class GenerateDraft
{
    public class Command : IRequest<Result<TimesheetDto>>
    {
        public required string RequestingUserId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int ProjectId { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<TimesheetDto>>
    {
        public async Task<Result<TimesheetDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (request.PeriodEnd < request.PeriodStart)
            {
                return ValidationFailure("Period", "Period end must be on or after period start.");
            }

            var profile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.UserId == request.RequestingUserId, cancellationToken);

            if (profile is null)
            {
                return ValidationFailure("EmployeeProfile", "No employee profile found for the current user.");
            }

            var project = await context.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.IsActive, cancellationToken);

            if (project is null)
            {
                return ValidationFailure("ProjectId", "The selected project is invalid or inactive.");
            }

            // Match the existing handler's day-bucket convention: events are
            // stored UTC-midnight-aligned by AttendanceController, so query
            // bounds in the same frame.
            var periodStart = NormalizeUtcDay(request.PeriodStart);
            var periodEnd = NormalizeUtcDay(request.PeriodEnd).AddDays(1);

            // Reuse an existing Draft for the same period if one is on file —
            // re-running generate-draft should be idempotent. Anything past
            // Draft (Submitted / Approved / Rejected) is off-limits.
            var existing = await context.Timesheets
                .Include(t => t.Entries)
                .FirstOrDefaultAsync(t =>
                    t.EmployeeProfileId == profile.Id
                    && t.PeriodStart == request.PeriodStart
                    && t.PeriodEnd == request.PeriodEnd, cancellationToken);

            if (existing is not null && existing.Status != TimesheetStatus.Draft)
            {
                return ValidationFailure("Timesheet",
                    "A timesheet for this period has already been submitted. Generate-draft only operates on Draft timesheets.");
            }

            var timesheet = existing ?? new Timesheet
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeProfileId = profile.Id,
                DepartmentId = profile.DepartmentId,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                Status = TimesheetStatus.Draft,
                TotalHours = 0,
                CreatedAt = DateTime.UtcNow,
            };

            if (existing is null)
            {
                context.Timesheets.Add(timesheet);
            }
            else
            {
                // Replace previous entries so the regenerated draft reflects
                // the current attendance state, not a stale snapshot.
                context.TimesheetEntries.RemoveRange(existing.Entries);
            }

            var events = await context.AttendanceEvents
                .AsNoTracking()
                .Where(e => e.EmployeeProfileId == profile.Id && e.At >= periodStart && e.At < periodEnd)
                .OrderBy(e => e.At)
                .ToListAsync(cancellationToken);

            var dailyHours = AttendanceHoursCalculator.Calculate(events);

            decimal totalHours = 0m;
            foreach (var day in dailyHours)
            {
                context.TimesheetEntries.Add(new TimesheetEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TimesheetId = timesheet.Id,
                    ProjectId = project.Id,
                    Date = day.Date,
                    HoursWorked = day.Hours,
                    Notes = "Auto-generated from attendance",
                });
                totalHours += day.Hours;
            }

            timesheet.TotalHours = totalHours;

            await context.SaveChangesAsync(cancellationToken);

            var user = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == request.RequestingUserId, cancellationToken);

            return Result<TimesheetDto>.Success(new TimesheetDto
            {
                Id = timesheet.Id,
                EmployeeId = timesheet.EmployeeProfileId,
                EmployeeName = user?.DisplayName ?? user?.UserName ?? timesheet.EmployeeProfileId,
                DepartmentId = timesheet.DepartmentId,
                PeriodStart = timesheet.PeriodStart,
                PeriodEnd = timesheet.PeriodEnd,
                TotalHours = timesheet.TotalHours,
                Status = timesheet.Status.ToString(),
                SubmittedAt = timesheet.SubmittedAt,
                ApprovedAt = timesheet.ApprovedAt,
                CreatedAt = timesheet.CreatedAt,
            });
        }

        private static DateTime NormalizeUtcDay(DateTime dt) =>
            new(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc);

        private static Result<TimesheetDto> ValidationFailure(string field, string message) =>
            Result<TimesheetDto>.ValidationFailure(
                new Dictionary<string, string[]> { [field] = new[] { message } },
                message);
    }
}
