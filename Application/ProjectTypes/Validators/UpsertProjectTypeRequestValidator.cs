using Application.ProjectTypes.DTOs;
using FluentValidation;

namespace Application.ProjectTypes.Validators;

public class UpsertProjectTypeRequestValidator : AbstractValidator<UpsertProjectTypeRequest>
{
    public UpsertProjectTypeRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Project type name is required.")
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Project type name is required.")
            .Must(name => name == name.Trim())
            .WithMessage("Project type name must not start or end with whitespace.")
            .MaximumLength(100)
            .WithMessage("Project type name must not exceed 100 characters.");

        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.Icon).MaximumLength(16);
        RuleFor(x => x.ColorKey).MaximumLength(30);
    }
}
