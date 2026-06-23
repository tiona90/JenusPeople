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
        public int? Page { get; set; }
        public int? PageSize { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Query, PagedResult<AnnualLeaveDto>>
    {
        public async Task<PagedResult<AnnualLeaveDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            IQueryable<AnnualLeave> annualLeavesQuery = context.AnnualLeaves
                .Include(al => al.Employee)
                .Include(al => al.Department)
                .AsNoTracking();

            if (request.IsAdmin)
            {
                // Admin sees everything.
            }
            else if (request.IsManager)
            {
                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.RequestingUserId,
                    cancellationToken);

                annualLeavesQuery = managerScope.ManagedDepartmentIds.Count == 0
                    ? annualLeavesQuery.Where(_ => false)
                    : annualLeavesQuery.Where(al =>
                        ((al.DepartmentId.HasValue && managerScope.ManagedDepartmentIds.Contains(al.DepartmentId.Value))
                         || managerScope.DirectReportUserIds.Contains(al.EmployeeId))
                        && (al.Employee == null || !al.Employee.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin)));
            }
            else if (request.IsEmployee)
            {
                annualLeavesQuery = annualLeavesQuery.Where(al => al.EmployeeId == request.RequestingUserId);
            }
            else
            {
                annualLeavesQuery = annualLeavesQuery.Where(_ => false);
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
