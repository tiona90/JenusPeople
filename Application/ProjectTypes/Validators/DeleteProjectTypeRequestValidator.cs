using Application.ProjectTypes.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectTypes.Validators;

public class DeleteProjectTypeRequestValidator : AbstractValidator<DeleteProjectType.Command>
{
    public DeleteProjectTypeRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.")
            .MustAsync(async (id, cancellationToken) =>
                await context.ProjectTypes.AnyAsync(t => t.Id == id, cancellationToken))
            .WithMessage("Project type not found.");
    }
}
