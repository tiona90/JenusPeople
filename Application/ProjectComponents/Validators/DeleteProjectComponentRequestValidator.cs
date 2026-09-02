using Application.ProjectComponents.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectComponents.Validators;

public class DeleteProjectComponentRequestValidator : AbstractValidator<DeleteProjectComponent.Command>
{
    public DeleteProjectComponentRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.")
            .MustAsync(async (id, cancellationToken) =>
                await context.ProjectComponents.AnyAsync(c => c.Id == id, cancellationToken))
            .WithMessage("Component not found.");
    }
}
