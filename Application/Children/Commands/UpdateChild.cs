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
                .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

            if (child is null)
                return Result<ChildDto>.Failure("Cannot find the child.");

            // Resolved through the filtered EmployeeProfiles set by the stored
            // foreign key, not through child.EmployeeProfile: that navigation comes
            // back null for a soft-deleted owner (EmployeeProfile carries a
            // !IsDeleted query filter that Child does not inherit), and null is
            // ChildAccessResolver's sentinel for "the caller themselves" -- so
            // reading the navigation would let any authenticated employee pass
            // as the owner of an orphaned child. Do not "simplify" this back to
            // child.EmployeeProfile?.UserId.
            var owner = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.Id == child.EmployeeProfileId, cancellationToken);

            if (owner is null)
                return Result<ChildDto>.Failure("Cannot find the child.");

            var access = await ChildAccessResolver.ResolveAsync(
                context, request.CallerUserId, owner.UserId,
                request.IsAdmin, request.IsManager, forWrite: true, cancellationToken);

            if (!access.IsSuccess)
                return Result<ChildDto>.FailureFrom(access);

            // A date-of-birth change moves the eligibility window (an entitlement
            // already granted against the old date could become one that never
            // should have been). Refused once leave has been recorded against the
            // child, same as DeleteChild -- unless the caller is an Admin, matching
            // how EditAnnualLeave lets an admin edit an approved request. A name
            // change carries no such risk and stays free.
            if (child.DateOfBirth != request.Child.DateOfBirth && !request.IsAdmin)
            {
                var hasLeave = await context.AnnualLeaves
                    .AnyAsync(leave => leave.ChildId == child.Id, cancellationToken);

                if (hasLeave)
                {
                    return Result<ChildDto>.Conflict(
                        $"{child.Name} has leave recorded against their current date of birth, so it cannot be changed here. " +
                        "Contact an administrator if it needs correcting.");
                }
            }

            child.Name = request.Child.Name.Trim();
            child.DateOfBirth = request.Child.DateOfBirth;

            await context.SaveChangesAsync(cancellationToken);

            var eligibleUntilAge = await ChildProjection.ResolveEligibleUntilAgeAsync(context, cancellationToken);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return Result<ChildDto>.Success(ChildProjection.ToDto(child, eligibleUntilAge, today));
        }
    }
}
