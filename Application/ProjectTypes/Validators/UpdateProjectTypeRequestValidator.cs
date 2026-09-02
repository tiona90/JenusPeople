using Application.ProjectTypes.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectTypes.Validators;

public class UpdateProjectTypeRequestValidator : AbstractValidator<UpdateProjectType.Command>
{
    public UpdateProjectTypeRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.")
            .MustAsync(async (id, cancellationToken) =>
                await context.ProjectTypes.AnyAsync(t => t.Id == id, cancellationToken))
            .WithMessage("Project type not found.");

        RuleFor(x => x.Type)
            .NotNull()
            .WithMessage("Project type payload is required.")
            .SetValidator(new UpsertProjectTypeRequestValidator());

        // Excludes the row being updated, so saving a type with its own name
        // unchanged is an edit rather than a collision with itself.
        RuleFor(x => x)
            .MustAsync(async (command, cancellationToken) =>
            {
                if (command.Type is null || string.IsNullOrWhiteSpace(command.Type.Name))
                {
                    return true;
                }

                var normalizedName = command.Type.Name.Trim().ToLower();
                return !await context.ProjectTypes.AnyAsync(
                    t => t.Id != command.Id && t.Name.ToLower() == normalizedName,
                    cancellationToken);
            })
            .WithMessage("A project type with that name already exists.");
    }
}
