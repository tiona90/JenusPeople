using Application.ProjectComponents.DTOs;
using FluentValidation;

namespace Application.ProjectComponents.Validators;

public class UpsertProjectComponentRequestValidator : AbstractValidator<UpsertProjectComponentRequest>
{
    public UpsertProjectComponentRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Component name is required.")
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Component name is required.")
            .Must(name => name == name.Trim())
            .WithMessage("Component name must not start or end with whitespace.")
            .MaximumLength(100)
            .WithMessage("Component name must not exceed 100 characters.");

        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.Icon).MaximumLength(16);
        RuleFor(x => x.ColorKey).MaximumLength(30);
    }
}
