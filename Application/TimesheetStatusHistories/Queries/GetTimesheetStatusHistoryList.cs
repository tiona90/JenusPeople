using Application.TimesheetStatusHistories.DTOs;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.TimesheetStatusHistories.Queries;

public class GetTimesheetStatusHistoryList
{
    public class Query : IRequest<List<TimesheetStatusHistoryDto>>
    {
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, List<TimesheetStatusHistoryDto>>
    {
        public async Task<List<TimesheetStatusHistoryDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            IQueryable<Domain.TimesheetStatusHistory> query = context.TimesheetStatusHistories
                .AsNoTracking()
                .Include(h => h.Timesheet)
                    .ThenInclude(t => t.Employee)
                        .ThenInclude(e => e!.User)
                .Include(h => h.ChangedByUser);

            if (request.IsAdmin)
            {
                // Admin sees all history.
            }
            else if (request.IsManager)
            {
                // TODO: Implement manager scope filtering if needed
            }
            else
            {
                // Regular user: only their own timesheet histories.
                // Timesheet.EmployeeId is the EmployeeProfile.Id, so we have to walk the
                // navigation to compare against the AspNetUsers.Id we get from the token.
                query = query.Where(h =>
                    h.Timesheet != null
                    && h.Timesheet.Employee != null
                    && h.Timesheet.Employee.UserId == request.RequestingUserId);
            }

            return await query
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new TimesheetStatusHistoryDto
                {
                    Id = h.Id,
                    TimesheetId = h.TimesheetId,
                    EmployeeId = h.Timesheet != null ? h.Timesheet.EmployeeId : string.Empty,
                    EmployeeName = h.Timesheet != null && h.Timesheet.Employee != null && h.Timesheet.Employee.User != null
                        ? (!string.IsNullOrWhiteSpace(h.Timesheet.Employee.User.DisplayName)
                            ? h.Timesheet.Employee.User.DisplayName
                            : (h.Timesheet.Employee.User.Email ?? h.Timesheet.EmployeeId))
                        : string.Empty,
                    ChangedByUserId = h.ChangedByUserId,
                    ChangedByUserName = h.ChangedByUser != null
                        ? (!string.IsNullOrWhiteSpace(h.ChangedByUser.DisplayName)
                            ? h.ChangedByUser.DisplayName
                            : (h.ChangedByUser.Email ?? h.ChangedByUserId))
                        : h.ChangedByUserId,
                    OldStatus = ((TimesheetStatus)h.FromStatus).ToString(),
                    NewStatus = ((TimesheetStatus)h.ToStatus).ToString(),
                    Comment = h.Comment,
                    ChangedAt = h.ChangedAt
                })
                .ToListAsync(cancellationToken);
        }
    }
}
