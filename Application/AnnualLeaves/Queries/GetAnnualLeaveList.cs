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
    public class Query : IRequest<List<AnnualLeaveDto>>
    {
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsEmployee { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Query, List<AnnualLeaveDto>>
    {
        public async Task<List<AnnualLeaveDto>> Handle(Query request, CancellationToken cancellationToken)
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

            var annualLeaves = await annualLeavesQuery.ToListAsync(cancellationToken);
            return mapper.Map<List<AnnualLeaveDto>>(annualLeaves);
        }
    }
}
