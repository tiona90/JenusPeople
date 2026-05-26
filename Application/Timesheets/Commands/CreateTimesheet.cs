using Application.Core;
using Application.Timesheets.DTOs;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Commands;

public class CreateTimesheet
{
    public class Command : IRequest<Result<TimesheetDto>>
    {
        public required string RequestingUserId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<TimesheetDto>>
    {
        public async Task<Result<TimesheetDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var profile = await context.EmployeeProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(ep => ep.UserId == request.RequestingUserId, cancellationToken);

            if (profile is null)
            {
                return Result<TimesheetDto>.ValidationFailure(
                    new Dictionary<string, string[]>
                    {
                        ["EmployeeProfile"] = ["No employee profile found for the current user."]
                    },
                    "No employee profile found for the current user.");
            }

            var timesheet = new Timesheet
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeId = profile.Id,
                DepartmentId = profile.DepartmentId,
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                Status = TimesheetStatus.Draft,
                TotalHours = 0,
                CreatedAt = DateTime.UtcNow
            };

            context.Timesheets.Add(timesheet);
            await context.SaveChangesAsync(cancellationToken);

            var user = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == request.RequestingUserId, cancellationToken);

            return Result<TimesheetDto>.Success(new TimesheetDto
            {
                Id = timesheet.Id,
                EmployeeId = timesheet.EmployeeId,
                EmployeeName = user?.DisplayName ?? user?.UserName ?? timesheet.EmployeeId,
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
    }
}
