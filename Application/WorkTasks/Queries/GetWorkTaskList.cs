using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Queries;

/// <summary>
/// Every task the caller can see. No filters: the page's tabs and filters run on
/// the client over this set, which it needs whole for the tab counts anyway.
/// </summary>
public class GetWorkTaskList
{
    public class Query : IRequest<Result<List<WorkTaskDto>>>
    {
        public string CallerUserId { get; set; } = string.Empty;
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<WorkTaskDto>>>
    {
        public async Task<Result<List<WorkTaskDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var departmentIds = await WorkTaskAccess.DepartmentIdsAsync(context, request.CallerUserId, cancellationToken);

            var tasks = await WorkTaskProjection
                .Project(context.WorkTasks.AsNoTracking().Where(t => departmentIds.Contains(t.DepartmentId)), request.CallerUserId)
                .ToListAsync(cancellationToken);

            return Result<List<WorkTaskDto>>.Success(WorkTaskProjection.Sort(tasks));
        }
    }
}
