using Application.Children.DTOs;
using Application.Children.Support;
using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Queries;

public class GetChildList
{
    public class Query : IRequest<Result<List<ChildDto>>>
    {
        /// <summary>The employee's user id. Null means the caller themselves.</summary>
        public string? EmployeeId { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<ChildDto>>>
    {
        public async Task<Result<List<ChildDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, request.EmployeeId,
                request.IsAdmin, request.IsManager, forWrite: false, cancellationToken);

            if (!access.IsSuccess)
                return Result<List<ChildDto>>.FailureFrom(access);

            var profileId = access.Value!.Id;

            var children = await context.Children
                .AsNoTracking()
                .Where(c => c.EmployeeProfileId == profileId)
                .OrderBy(c => c.DateOfBirth)
                .ToListAsync(cancellationToken);

            var eligibleUntilAge = await ChildProjection.ResolveEligibleUntilAgeAsync(context, cancellationToken);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Oldest first: it is the order a parent lists their children in, and it
            // puts the child closest to aging out at the top.
            var dtos = children
                .Select(c => ChildProjection.ToDto(c, eligibleUntilAge, today))
                .ToList();

            return Result<List<ChildDto>>.Success(dtos);
        }
    }
}
