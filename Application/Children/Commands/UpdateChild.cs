using Application.Children.DTOs;
using Application.Children.Support;
using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Commands;

public class UpdateChild
{
    public class Command : IRequest<Result<ChildDto>>
    {
        public required string Id { get; set; }
        public required UpsertChildRequest Child { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<ChildDto>>
    {
        public async Task<Result<ChildDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var child = await context.Children
                .Include(c => c.EmployeeProfile)
                .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

            if (child is null)
                return Result<ChildDto>.Failure("Cannot find the child.");

            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, child.EmployeeProfile?.UserId,
                request.IsAdmin, request.IsManager, forWrite: true, cancellationToken);

            if (!access.IsSuccess)
                return Result<ChildDto>.FailureFrom(access);

            child.Name = request.Child.Name.Trim();
            child.DateOfBirth = request.Child.DateOfBirth;

            await context.SaveChangesAsync(cancellationToken);

            return Result<ChildDto>.Success(ChildProjection.ToDto(child, ChildProjection.DefaultEligibilityAge));
        }
    }
}
