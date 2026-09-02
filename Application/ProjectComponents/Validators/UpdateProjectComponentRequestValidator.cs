using Application.ProjectComponents.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectComponents.Validators;

public class UpdateProjectComponentRequestValidator : AbstractValidator<UpdateProjectComponent.Command>
{
    public UpdateProjectComponentRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.")
            .MustAsync(async (id, cancellationToken) =>
                await context.ProjectComponents.AnyAsync(c => c.Id == id, cancellationToken))
            .WithMessage("Component not found.");

        RuleFor(x => x.Component)
            .NotNull()
            .WithMessage("Component payload is required.")
            .SetValidator(new UpsertProjectComponentRequestValidator());

        // Excludes the row being updated, so saving a component with its own name
        // unchanged is an edit rather than a collision with itself.
        RuleFor(x => x)
            .MustAsync(async (command, cancellationToken) =>
            {
                if (command.Component is null || string.IsNullOrWhiteSpace(command.Component.Name))
                {
                    return true;
                }

                var normalizedName = command.Component.Name.Trim().ToLower();
                return !await context.ProjectComponents.AnyAsync(
                    c => c.Id != command.Id && c.Name.ToLower() == normalizedName,
                    cancellationToken);
            })
            .WithMessage("A component with that name already exists.");
    }
}
