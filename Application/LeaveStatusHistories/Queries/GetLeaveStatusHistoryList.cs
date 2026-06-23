using Application.LeaveStatusHistories.DTOs;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.LeaveStatusHistories.Queries;

public class GetLeaveStatusHistoryList
{
    public class Query : IRequest<PagedResult<LeaveStatusHistoryDto>>
    {
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, PagedResult<LeaveStatusHistoryDto>>
    {
        public async Task<PagedResult<LeaveStatusHistoryDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            IQueryable<Domain.LeaveStatusHistory> query = context.LeaveStatusHistories
                .AsNoTracking()
                .Include(h => h.AnnualLeave)
                    .ThenInclude(a => a!.Employee)
                .Include(h => h.AnnualLeave)
                    .ThenInclude(a => a!.LeaveType);

            if (request.IsAdmin)
            {
                // Admin sees all history.
            }
            else if (request.IsManager)
            {
                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.RequestingUserId,
                    cancellationToken);

                query = query.Where(h =>
                    h.AnnualLeave != null &&
                    ((h.AnnualLeave.DepartmentId.HasValue &&
                      managerScope.ManagedDepartmentIds.Contains(h.AnnualLeave.DepartmentId.Value))
                     || managerScope.DirectReportUserIds.Contains(h.AnnualLeave.EmployeeId))
                    && (h.AnnualLeave.Employee == null || !h.AnnualLeave.Employee.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin)));
            }
            else
            {
                // Employee sees only history for their own leaves.
                query = query.Where(h =>
                    h.AnnualLeave != null &&
                    h.AnnualLeave.EmployeeId == request.RequestingUserId);
            }

            var total = await query.CountAsync(cancellationToken);

            var projected = query
                .OrderByDescending(h => h.ChangedAt)
                .ThenBy(h => h.Id)
                .Select(h => new LeaveStatusHistoryDto
                {
                    Id = h.Id,
                    AnnualLeaveId = h.AnnualLeaveId,
                    EmployeeId = h.AnnualLeave != null ? h.AnnualLeave.EmployeeId : string.Empty,
                    EmployeeName = h.AnnualLeave != null && h.AnnualLeave.Employee != null
                        ? (!string.IsNullOrWhiteSpace(h.AnnualLeave.Employee.DisplayName)
                            ? h.AnnualLeave.Employee.DisplayName
                            : (h.AnnualLeave.Employee.Email ?? h.AnnualLeave.EmployeeId))
                        : string.Empty,
                    LeaveTypeName = h.AnnualLeave != null && h.AnnualLeave.LeaveType != null
                        ? h.AnnualLeave.LeaveType.Name
                        : null,
                    ChangedByUserId = h.ChangedByUserId,
                    ChangedByUserName = h.ChangedByUser != null
                        ? (!string.IsNullOrWhiteSpace(h.ChangedByUser.DisplayName)
                            ? h.ChangedByUser.DisplayName
                            : (h.ChangedByUser.Email ?? h.ChangedByUserId))
                        : h.ChangedByUserId,
                    OldStatus = h.OldStatus.HasValue ? h.OldStatus.Value.ToString() : null,
                    NewStatus = h.NewStatus.ToString(),
                    Comment = h.Comment,
                    ChangedAt = h.ChangedAt
                });

            var paging = Pagination.Resolve(request.Page, request.PageSize);
            if (paging is { } pg)
                projected = projected.Skip((pg.Page - 1) * pg.Size).Take(pg.Size);

            return new PagedResult<LeaveStatusHistoryDto>
            {
                Items = await projected.ToListAsync(cancellationToken),
                Total = total,
                Page = paging?.Page,
                PageSize = paging?.Size,
            };
        }
    }
}
