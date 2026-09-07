using Application.EmployeeProfiles.DTOs;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.EmployeeProfiles.Queries;

public class GetEmployeeProfileList
{
    public class Query : IRequest<List<EmployeeProfileDto>>
    {
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, List<EmployeeProfileDto>>
    {
        public async Task<List<EmployeeProfileDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            IQueryable<Domain.EmployeeProfile> query = context.EmployeeProfiles
                .AsNoTracking()
                .Include(ep => ep.User);

            if (request.IsAdmin)
            {
                // Admin sees all profiles.
            }
            else if (request.IsManager)
            {
                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.RequestingUserId,
                    cancellationToken);

                // Restrict to only employees in the manager's own department(s)
                query = query.Where(ep =>
                    ((ep.DepartmentId != null && managerScope.ManagedDepartmentIds.Contains(ep.DepartmentId.Value))
                     || (ep.ManagerId != null && managerScope.ManagerProfileIds.Contains(ep.ManagerId))
                     || ep.UserId == request.RequestingUserId)
                    && (ep.User == null || !ep.User.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin)));
            }
            else
            {
                // Employee sees only their own profile.
                query = query.Where(ep => ep.UserId == request.RequestingUserId);
            }

            return await query
                .OrderBy(ep => ep.UserId)
                .Select(ep => new EmployeeProfileDto
                {
                    Id = ep.Id,
                    UserId = ep.UserId,
                    DisplayName = ep.User != null
                        ? (ep.User.DisplayName ?? ep.User.UserName ?? ep.UserId)
                        : ep.UserId,
                    DepartmentId = ep.DepartmentId,
                    ManagerId = ep.ManagerId,
                    AnnualLeaveEntitlement = ep.AnnualLeaveEntitlement,
                    LeaveBalance = ep.LeaveBalance,
                    JobTitle = ep.JobTitle,
                    CreatedAt = ep.CreatedAt
                })
                .ToListAsync(cancellationToken);
        }
    }
}
