using System;
using System.Threading.Tasks;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Queries;

public class GetAnnualLeaveList
{
    public class Query : IRequest<PagedResult<AnnualLeaveDto>>
    {
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsEmployee { get; set; }

        /// <summary>
        /// The HR Administrator also reaches department-less leave — the
        /// administrators' own — which no department scope would otherwise include.
        /// </summary>
        public bool IsHrAdministrator { get; set; }
        public int? Page { get; set; }
        public int? PageSize { get; set; }

        /// <summary>
        /// Only requests still waiting on a decision (Pending, AwaitingHrApproval).
        /// The notification bell lists nothing else, and polling every leave in
        /// scope, for all time, to find them cost far more than the bell showed.
        /// </summary>
        public bool OpenOnly { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Query, PagedResult<AnnualLeaveDto>>
    {
        public async Task<PagedResult<AnnualLeaveDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            IQueryable<AnnualLeave> annualLeavesQuery = context.AnnualLeaves
                .Include(al => al.Employee)
                .Include(al => al.Department)
                .Include(al => al.Delegate)
                .Include(al => al.Child)
                .AsNoTracking();

            if (request.IsAdmin)
            {
                // System Administrator sees everything.
            }
            else if (request.IsManager)
            {
                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.RequestingUserId,
                    cancellationToken);

                annualLeavesQuery = managerScope.ManagedDepartmentIds.Count == 0 && !request.IsHrAdministrator
                    ? annualLeavesQuery.Where(_ => false)
                    : annualLeavesQuery.Where(al =>
                        (((al.DepartmentId.HasValue && managerScope.ManagedDepartmentIds.Contains(al.DepartmentId.Value))
                          || managerScope.DirectReportUserIds.Contains(al.EmployeeId))
                         && (al.Employee == null || !al.Employee.UserRoles.Any(ur => ur.Role != null && AppRoles.Administrators.Contains(ur.Role.Name!))))
                        // The HR Administrator also reaches department-less leave — the
                        // administrators' own, which nobody's assigned departments cover.
                        || (request.IsHrAdministrator && al.DepartmentId == null));
            }
            else if (request.IsEmployee)
            {
                annualLeavesQuery = annualLeavesQuery.Where(al => al.EmployeeId == request.RequestingUserId);
            }
            else
            {
                annualLeavesQuery = annualLeavesQuery.Where(_ => false);
            }

            if (request.OpenOnly)
            {
                annualLeavesQuery = annualLeavesQuery.Where(al =>
                    al.Status == AnnualLeaveStatus.Pending || al.Status == AnnualLeaveStatus.AwaitingHrApproval);
            }

            // Filtering above runs in SQL. Count the filtered set, then apply a
            // deterministic order + optional paging (also in SQL) before materializing.
            var total = await annualLeavesQuery.CountAsync(cancellationToken);

            var ordered = annualLeavesQuery
                .OrderByDescending(al => al.StartDate)
                .ThenBy(al => al.Id);

            var paging = Pagination.Resolve(request.Page, request.PageSize);
            IQueryable<AnnualLeave> pageQuery = ordered;
            if (paging is { } pg)
                pageQuery = ordered.Skip((pg.Page - 1) * pg.Size).Take(pg.Size);

            var annualLeaves = await pageQuery.ToListAsync(cancellationToken);
            return new PagedResult<AnnualLeaveDto>
            {
                Items = mapper.Map<List<AnnualLeaveDto>>(annualLeaves),
                Total = total,
                Page = paging?.Page,
                PageSize = paging?.Size,
            };
        }
    }
}
