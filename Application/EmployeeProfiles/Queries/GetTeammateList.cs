using Application.EmployeeProfiles.DTOs;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.EmployeeProfiles.Queries;

/// <summary>
/// Colleagues the requesting user shares a department with, minus themselves.
/// Returns names and job titles only, so any authenticated user can call it —
/// it backs the "nominate someone to cover my leave" picker.
/// </summary>
public class GetTeammateList
{
    public class Query : IRequest<List<TeammateDto>>
    {
        public string RequestingUserId { get; set; } = string.Empty;
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, List<TeammateDto>>
    {
        public async Task<List<TeammateDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.RequestingUserId)) return [];

            var myDepartmentId = await context.EmployeeProfiles
                .AsNoTracking()
                .Where(ep => ep.UserId == request.RequestingUserId)
                .Select(ep => (int?)ep.DepartmentId)
                .FirstOrDefaultAsync(cancellationToken);

            if (myDepartmentId is null) return [];

            return await context.EmployeeProfiles
                .AsNoTracking()
                .Include(ep => ep.User)
                .Where(ep =>
                    ep.DepartmentId == myDepartmentId
                    && ep.UserId != request.RequestingUserId
                    && (ep.User == null || !ep.User.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin)))
                .Select(ep => new TeammateDto
                {
                    UserId = ep.UserId,
                    DisplayName = ep.User != null
                        ? (ep.User.DisplayName ?? ep.User.UserName ?? ep.UserId)
                        : ep.UserId,
                    JobTitle = ep.JobTitle,
                    DepartmentId = ep.DepartmentId,
                })
                .OrderBy(t => t.DisplayName)
                .ToListAsync(cancellationToken);
        }
    }
}
