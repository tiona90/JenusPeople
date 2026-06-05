using Application.ProjectActivityTypes.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectActivityTypes.Validators;

public class DeleteProjectActivityTypeRequestValidator : AbstractValidator<DeleteProjectActivityType.Command>
{
    public DeleteProjectActivityTypeRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.")
            .MustAsync(async (id, cancellationToken) =>
                await context.ProjectActivityTypes.AnyAsync(a => a.Id == id, cancellationToken))
            .WithMessage("Activity type not found.");

        RuleFor(x => x.Id)
            .MustAsync(async (id, cancellationToken) =>
                !await context.TimesheetEntries.AnyAsync(e => e.ActivityTypeId == id, cancellationToken))
            .WithMessage("Cannot delete activity type because it is used by timesheet entries.");
    }
}
