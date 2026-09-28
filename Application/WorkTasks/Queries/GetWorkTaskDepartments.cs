using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Queries;

/// <summary>The departments the caller may file a task under — their scope, by name.</summary>
public class GetWorkTaskDepartments
{
    public class Query : IRequest<Result<List<WorkTaskDepartmentDto>>>
    {
        public string CallerUserId { get; set; } = string.Empty;
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<WorkTaskDepartmentDto>>>
    {
        public async Task<Result<List<WorkTaskDepartmentDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var departmentIds = await WorkTaskAccess.DepartmentIdsAsync(context, request.CallerUserId, cancellationToken);

            var departments = await context.Departments
                .AsNoTracking()
                .Where(d => departmentIds.Contains(d.Id))
                .OrderBy(d => d.Name)
                .Select(d => new WorkTaskDepartmentDto { Id = d.Id, Name = d.Name })
                .ToListAsync(cancellationToken);

            return Result<List<WorkTaskDepartmentDto>>.Success(departments);
        }
    }
}
