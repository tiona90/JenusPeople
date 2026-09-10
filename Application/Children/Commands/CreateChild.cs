using Application.Children.DTOs;
using Application.Children.Support;
using Application.Core;
using Domain;
using MediatR;
using Persistence;

namespace Application.Children.Commands;

public class CreateChild
{
    public class Command : IRequest<Result<ChildDto>>
    {
        /// <summary>The employee's user id. Null means the caller themselves.</summary>
        public string? EmployeeId { get; set; }
        public required UpsertChildRequest Child { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<ChildDto>>
    {
        public async Task<Result<ChildDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, request.EmployeeId,
                request.IsAdmin, request.IsManager, forWrite: true, cancellationToken);

            if (!access.IsSuccess)
                return Result<ChildDto>.FailureFrom(access);

            var profile = access.Value!;

            var child = new Child
            {
                EmployeeProfileId = profile.Id,
                Name = request.Child.Name.Trim(),
                DateOfBirth = request.Child.DateOfBirth,
            };

            context.Children.Add(child);

            // The declaration and the list cannot be allowed to disagree: adding a
            // child answers "do you have children" whatever the flag said before.
            profile.HasChildren = true;

            await context.SaveChangesAsync(cancellationToken);

            var eligibleUntilAge = await ChildProjection.ResolveEligibleUntilAgeAsync(context, cancellationToken);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return Result<ChildDto>.Success(ChildProjection.ToDto(child, eligibleUntilAge, today));
        }
    }
}
