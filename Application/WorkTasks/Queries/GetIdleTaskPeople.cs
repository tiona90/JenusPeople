using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Queries;

/// <summary>
/// Who in the caller's departments is not working on anything: every person a task
/// there could be given to (<see cref="WorkTaskAssigneeRule.EligibleAsync"/> — active
/// Managers and Employees) with no task In Progress. Somebody with tasks still in
/// To Do is listed too, since none of it has started; the counts say which is which.
/// The caller is left out. Counts cover every task the person is on, wherever it
/// sits: somebody busy in another department is busy.
/// </summary>
public class GetIdleTaskPeople
{
    public class Query : IRequest<Result<List<WorkTaskIdlePersonDto>>>
    {
        public string CallerUserId { get; set; } = string.Empty;
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<WorkTaskIdlePersonDto>>>
    {
        public async Task<Result<List<WorkTaskIdlePersonDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var departmentIds = await WorkTaskAccess.DepartmentIdsAsync(context, request.CallerUserId, cancellationToken);
            var departmentNames = await context.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

            // One person may cover several of the caller's departments; they are listed once.
            var people = new Dictionary<string, WorkTaskIdlePersonDto>();
            foreach (var departmentId in departmentIds.Distinct().OrderBy(id => departmentNames.GetValueOrDefault(id)))
            {
                foreach (var person in await WorkTaskAssigneeRule.EligibleAsync(context, departmentId, cancellationToken))
                {
                    if (person.UserId == request.CallerUserId) continue;
                    if (!people.TryGetValue(person.UserId, out var row))
                    {
                        row = new WorkTaskIdlePersonDto { UserId = person.UserId, DisplayName = person.DisplayName };
                        people[person.UserId] = row;
                    }
                    row.DepartmentIds.Add(departmentId);
                    row.DepartmentNames.Add(departmentNames.GetValueOrDefault(departmentId, ""));
                }
            }
            if (people.Count == 0)
                return Result<List<WorkTaskIdlePersonDto>>.Success([]);

            var userIds = people.Keys.ToList();
            var counts = await context.WorkTaskAssignees
                .AsNoTracking()
                .Where(a => userIds.Contains(a.UserId)
                    && (a.WorkTask!.Status == WorkTaskStatus.ToDo || a.WorkTask.Status == WorkTaskStatus.InProgress))
                .GroupBy(a => new { a.UserId, a.WorkTask!.Status })
                .Select(g => new { g.Key.UserId, g.Key.Status, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var busy = counts.Where(c => c.Status == WorkTaskStatus.InProgress && c.Count > 0).Select(c => c.UserId).ToHashSet();
            foreach (var c in counts.Where(c => c.Status == WorkTaskStatus.ToDo))
                people[c.UserId].ToDoCount = c.Count;

            var roles = await (
                    from ur in context.UserRoles
                    join r in context.Roles on ur.RoleId equals r.Id
                    where userIds.Contains(ur.UserId) && r.Name == AppRoles.Manager
                    select ur.UserId)
                .ToListAsync(cancellationToken);
            var managers = roles.ToHashSet();

            var idle = people.Values
                .Where(p => !busy.Contains(p.UserId))
                .Select(p => { p.IsManager = managers.Contains(p.UserId); return p; })
                // Nothing at all first — the clearest gap — then by name.
                .OrderBy(p => p.ToDoCount > 0 ? 1 : 0)
                .ThenBy(p => p.DisplayName)
                .ToList();

            return Result<List<WorkTaskIdlePersonDto>>.Success(idle);
        }
    }
}
