using Application.ProjectActivityTypes.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectActivityTypes.Validators;

public class CreateProjectActivityTypeRequestValidator : AbstractValidator<CreateProjectActivityType.Command>
{
    public CreateProjectActivityTypeRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.ActivityType)
            .NotNull()
            .WithMessage("ActivityType payload is required.")
            .SetValidator(new UpsertProjectActivityTypeRequestValidator());

        RuleFor(x => x.ActivityType.Name)
            .MustAsync(async (name, cancellationToken) =>
            {
                var normalizedName = name.Trim().ToLower();
                return !await context.ProjectActivityTypes.AnyAsync(a => a.Name.ToLower() == normalizedName, cancellationToken);
            })
            .WithMessage("An activity type with that name already exists.")
            .When(x => x.ActivityType is not null && !string.IsNullOrWhiteSpace(x.ActivityType.Name));
    }
}
