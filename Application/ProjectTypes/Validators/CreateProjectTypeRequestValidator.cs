using Application.ProjectTypes.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectTypes.Validators;

public class CreateProjectTypeRequestValidator : AbstractValidator<CreateProjectType.Command>
{
    public CreateProjectTypeRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Type)
            .NotNull()
            .WithMessage("Project type payload is required.")
            .SetValidator(new UpsertProjectTypeRequestValidator());

        RuleFor(x => x.Type.Name)
            .MustAsync(async (name, cancellationToken) =>
            {
                var normalizedName = name.Trim().ToLower();
                return !await context.ProjectTypes.AnyAsync(t => t.Name.ToLower() == normalizedName, cancellationToken);
            })
            .WithMessage("A project type with that name already exists.")
            .When(x => x.Type is not null && !string.IsNullOrWhiteSpace(x.Type.Name));
    }
}
