using Application.ProjectActivityTypes.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectActivityTypes.Validators;

public class UpdateProjectActivityTypeRequestValidator : AbstractValidator<UpdateProjectActivityType.Command>
{
    public UpdateProjectActivityTypeRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.")
            .MustAsync(async (id, cancellationToken) =>
                await context.ProjectActivityTypes.AnyAsync(a => a.Id == id, cancellationToken))
            .WithMessage("Activity type not found.");

        RuleFor(x => x.ActivityType)
            .NotNull()
            .WithMessage("ActivityType payload is required.")
            .SetValidator(new UpsertProjectActivityTypeRequestValidator());

        RuleFor(x => x)
            .MustAsync(async (command, cancellationToken) =>
            {
                if (command.ActivityType is null || string.IsNullOrWhiteSpace(command.ActivityType.Name))
                {
                    return true;
                }

                var normalizedName = command.ActivityType.Name.Trim().ToLower();
                return !await context.ProjectActivityTypes.AnyAsync(
                    a => a.Id != command.Id && a.Name.ToLower() == normalizedName,
                    cancellationToken);
            })
            .WithMessage("An activity type with that name already exists.");
    }
}
