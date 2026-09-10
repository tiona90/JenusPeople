using Application.LeaveTypes.Commands;
using Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.LeaveTypes.Validators;

public class DeleteLeaveTypeRequestValidator : AbstractValidator<DeleteLeaveType.Command>
{
    public DeleteLeaveTypeRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.")
            .MustAsync(async (id, cancellationToken) =>
                await context.LeaveTypes.AnyAsync(lt => lt.Id == id, cancellationToken))
            .WithMessage("Leave type not found.");

        // The seeded types are not deletable at all — seeding does not restore one an
        // admin removed. DeleteLeaveType re-checks; this is what produces the message.
        RuleFor(x => x.Id)
            .MustAsync(async (id, cancellationToken) =>
            {
                var name = await context.LeaveTypes
                    .Where(lt => lt.Id == id)
                    .Select(lt => lt.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                return !SystemLeaveTypes.IsSystem(name);
            })
            .WithMessage("That is a built-in leave type and cannot be deleted.");

        RuleFor(x => x.Id)
            .MustAsync(async (id, cancellationToken) =>
                !await context.AnnualLeaves.AnyAsync(al => al.LeaveTypeId == id, cancellationToken))
            .WithMessage("Cannot delete leave type because it is used by leave requests.");
    }
}
