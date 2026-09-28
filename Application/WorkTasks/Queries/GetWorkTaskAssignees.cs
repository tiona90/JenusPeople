using Application.Core;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using MediatR;
using Persistence;

namespace Application.WorkTasks.Queries;

public class GetWorkTaskAssignees
{
    public class Query : IRequest<Result<List<WorkTaskAssigneeDto>>>
    {
        public string CallerUserId { get; set; } = string.Empty;
        public int DepartmentId { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<WorkTaskAssigneeDto>>>
    {
        public async Task<Result<List<WorkTaskAssigneeDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var departmentIds = await WorkTaskAccess.DepartmentIdsAsync(context, request.CallerUserId, cancellationToken);
            if (!departmentIds.Contains(request.DepartmentId))
                return Result<List<WorkTaskAssigneeDto>>.Failure("Department not found.");

            return Result<List<WorkTaskAssigneeDto>>.Success(
                await WorkTaskAssigneeRule.EligibleAsync(context, request.DepartmentId, cancellationToken));
        }
    }
}
