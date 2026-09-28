using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using MediatR;
using Persistence;

namespace Application.WorkTasks.Queries;

/// <summary>The projects a task in one of the caller's departments may name.</summary>
public class GetWorkTaskProjects
{
    public class Query : IRequest<Result<List<WorkTaskProjectDto>>>
    {
        public string CallerUserId { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<WorkTaskProjectDto>>>
    {
        public async Task<Result<List<WorkTaskProjectDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var departmentIds = await WorkTaskAccess.DepartmentIdsAsync(context, request.CallerUserId, cancellationToken);
            if (!departmentIds.Contains(request.DepartmentId))
                return Result<List<WorkTaskProjectDto>>.Failure("Department not found.");

            return Result<List<WorkTaskProjectDto>>.Success(
                await WorkTaskProjectRule.AvailableAsync(context, request.DepartmentId, cancellationToken));
        }
    }
}
