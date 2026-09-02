using Application.ProjectComponents.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectComponents.Validators;

public class CreateProjectComponentRequestValidator : AbstractValidator<CreateProjectComponent.Command>
{
    public CreateProjectComponentRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Component)
            .NotNull()
            .WithMessage("Component payload is required.")
            .SetValidator(new UpsertProjectComponentRequestValidator());

        RuleFor(x => x.Component.Name)
            .MustAsync(async (name, cancellationToken) =>
            {
                var normalizedName = name.Trim().ToLower();
                return !await context.ProjectComponents.AnyAsync(c => c.Name.ToLower() == normalizedName, cancellationToken);
            })
            .WithMessage("A component with that name already exists.")
            .When(x => x.Component is not null && !string.IsNullOrWhiteSpace(x.Component.Name));
    }
}
