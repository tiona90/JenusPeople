using Application.Children.Support;
using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Commands;

public class DeleteChild
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var child = await context.Children
                .Include(c => c.EmployeeProfile)
                .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

            if (child is null)
                return Result<Unit>.Failure("Cannot find the child.");

            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, child.EmployeeProfile?.UserId,
                request.IsAdmin, request.IsManager, forWrite: true, cancellationToken);

            if (!access.IsSuccess)
                return Result<Unit>.FailureFrom(access);

            /* Refused rather than cascaded: the child's row is what the per-child
               ledger is queried by, so deleting it would erase the record of leave
               that was actually taken. The database says the same thing (the FK is
               Restrict) — this is the version with an explanation. A child who has
               aged out is kept, not deleted; they read as ineligible. */
            var leaveCount = await context.AnnualLeaves
                .CountAsync(leave => leave.ChildId == child.Id, cancellationToken);

            if (leaveCount > 0)
            {
                return Result<Unit>.Conflict(
                    $"{child.Name} has {leaveCount} leave request(s) recorded against them and cannot be removed. " +
                    "A child who is no longer eligible is kept on the profile rather than deleted.");
            }

            context.Children.Remove(child);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
