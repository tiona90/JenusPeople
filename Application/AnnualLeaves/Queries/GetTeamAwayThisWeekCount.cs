using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Queries;

public class GetTeamAwayThisWeekCount
{
    public class Query : IRequest<int>
    {
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsEmployee { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, int>
    {
        public async Task<int> Handle(Query request, CancellationToken cancellationToken)
        {
            var today = DateTime.Today;
            var daysSinceMonday = today.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)today.DayOfWeek - 1;
            var weekStart = today.AddDays(-daysSinceMonday);
            var weekEnd = weekStart.AddDays(6);

            IQueryable<AnnualLeave> query = context.AnnualLeaves
                .AsNoTracking()
                .Where(al => al.Status == AnnualLeaveStatus.Approved)
                .Where(al => al.StartDate.Date <= weekEnd && al.EndDate.Date >= weekStart);

            if (request.IsAdmin)
            {
                // Admin sees all approved employees away this week.
            }
            else if (request.IsManager)
            {
                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.RequestingUserId,
                    cancellationToken);

                query = managerScope.ManagedDepartmentIds.Count == 0
                    ? query.Where(_ => false)
                    : query.Where(al =>
                        ((al.DepartmentId.HasValue && managerScope.ManagedDepartmentIds.Contains(al.DepartmentId.Value))
                         || managerScope.DirectReportUserIds.Contains(al.EmployeeId))
                        && (al.Employee == null || !al.Employee.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin)));
            }
            else if (request.IsEmployee)
            {
                var profileDepartmentId = await context.EmployeeProfiles
                    .Where(ep => ep.UserId == request.RequestingUserId)
                    .Select(ep => (int?)ep.DepartmentId)
                    .FirstOrDefaultAsync(cancellationToken);

                query = profileDepartmentId.HasValue
                    ? query.Where(al => al.DepartmentId == profileDepartmentId.Value)
                    : query.Where(_ => false);
            }
            else
            {
                query = query.Where(_ => false);
            }

            return await query
                .Select(al => al.EmployeeId)
                .Distinct()
                .CountAsync(cancellationToken);
        }
    }
}