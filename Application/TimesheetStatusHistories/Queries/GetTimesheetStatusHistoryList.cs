using Application.TimesheetStatusHistories.DTOs;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.TimesheetStatusHistories.Queries;

public class GetTimesheetStatusHistoryList
{
    public class Query : IRequest<PagedResult<TimesheetStatusHistoryDto>>
    {
        /// <summary>
        /// Optional: restrict to one timesheet's history. The scope filter below
        /// still applies, so asking for a timesheet you may not see returns nothing
        /// rather than someone else's audit trail.
        /// </summary>
        public string? TimesheetId { get; set; }

        /// <summary>
        /// Optional: restrict to one employee's history. This is an
        /// <see cref="Domain.EmployeeProfile"/>.Id — the key
        /// <see cref="Domain.Timesheet.EmployeeProfileId"/> holds — and not an
        /// AspNetUsers.Id. The scope filter below still applies, so asking for an
        /// employee you may not see returns nothing rather than their audit trail.
        /// </summary>
        public string? EmployeeProfileId { get; set; }

        /// <summary>Optional: restrict to timesheets belonging to one department.</summary>
        public int? DepartmentId { get; set; }

        /// <summary>Optional: only changes recorded at or after this instant.</summary>
        public DateTime? From { get; set; }

        /// <summary>Optional: only changes recorded at or before this instant.</summary>
        public DateTime? To { get; set; }

        /// <summary>
        /// Optional: only transitions out of this <see cref="Domain.TimesheetStatus"/>,
        /// as its underlying int.
        /// </summary>
        public int? FromStatus { get; set; }

        /// <summary>
        /// Optional: only transitions into this <see cref="Domain.TimesheetStatus"/>,
        /// as its underlying int.
        /// </summary>
        public int? ToStatus { get; set; }

        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, PagedResult<TimesheetStatusHistoryDto>>
    {
        public async Task<PagedResult<TimesheetStatusHistoryDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            IQueryable<Domain.TimesheetStatusHistory> query = context.TimesheetStatusHistories
                .AsNoTracking()
                .Include(h => h.Timesheet)
                    .ThenInclude(t => t.Employee)
                        .ThenInclude(e => e!.User)
                .Include(h => h.ChangedByUser);

            if (!string.IsNullOrWhiteSpace(request.TimesheetId))
            {
                query = query.Where(h => h.TimesheetId == request.TimesheetId);
            }

            if (!string.IsNullOrWhiteSpace(request.EmployeeProfileId))
            {
                query = query.Where(h =>
                    h.Timesheet != null
                    && h.Timesheet.EmployeeProfileId == request.EmployeeProfileId);
            }

            if (request.DepartmentId is { } departmentId)
            {
                query = query.Where(h =>
                    h.Timesheet != null && h.Timesheet.DepartmentId == departmentId);
            }

            if (request.From is { } from)
            {
                query = query.Where(h => h.ChangedAt >= from);
            }

            if (request.To is { } to)
            {
                query = query.Where(h => h.ChangedAt <= to);
            }

            if (request.FromStatus is { } fromStatus)
            {
                query = query.Where(h => h.FromStatus == fromStatus);
            }

            if (request.ToStatus is { } toStatus)
            {
                query = query.Where(h => h.ToStatus == toStatus);
            }

            if (request.IsAdmin)
            {
                // Admin sees all history.
            }
            else if (request.IsManager)
            {
                // Scope to the manager's own history, their managed departments, and
                // their direct reports — mirroring GetTimesheetList's authority.
                var scope = await ManagerAccessScopeResolver.ResolveAsync(
                    context, request.RequestingUserId, cancellationToken);

                query = query.Where(h =>
                    h.Timesheet != null
                    && h.Timesheet.Employee != null
                    && (h.Timesheet.Employee.UserId == request.RequestingUserId
                        || scope.ManagedDepartmentIds.Contains(h.Timesheet.DepartmentId)
                        || scope.DirectReportUserIds.Contains(h.Timesheet.Employee.UserId)));
            }
            else
            {
                // Regular user: only their own timesheet histories.
                // Timesheet.EmployeeProfileId is the EmployeeProfile.Id, so we have to walk the
                // navigation to compare against the AspNetUsers.Id we get from the token.
                query = query.Where(h =>
                    h.Timesheet != null
                    && h.Timesheet.Employee != null
                    && h.Timesheet.Employee.UserId == request.RequestingUserId);
            }

            var total = await query.CountAsync(cancellationToken);

            var projected = query
                .OrderByDescending(h => h.ChangedAt)
                .ThenBy(h => h.Id)
                .Select(h => new TimesheetStatusHistoryDto
                {
                    Id = h.Id,
                    TimesheetId = h.TimesheetId,
                    EmployeeId = h.Timesheet != null ? h.Timesheet.EmployeeProfileId : string.Empty,
                    EmployeeName = h.Timesheet != null && h.Timesheet.Employee != null && h.Timesheet.Employee.User != null
                        ? (!string.IsNullOrWhiteSpace(h.Timesheet.Employee.User.DisplayName)
                            ? h.Timesheet.Employee.User.DisplayName
                            : (h.Timesheet.Employee.User.Email ?? h.Timesheet.EmployeeProfileId))
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
                });

            var paging = Pagination.Resolve(request.Page, request.PageSize);
            if (paging is { } pg)
                projected = projected.Skip((pg.Page - 1) * pg.Size).Take(pg.Size);

            return new PagedResult<TimesheetStatusHistoryDto>
            {
                Items = await projected.ToListAsync(cancellationToken),
                Total = total,
                Page = paging?.Page,
                PageSize = paging?.Size,
            };
        }
    }
}
