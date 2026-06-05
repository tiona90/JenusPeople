using Application.ProjectActivityTypes.DTOs;
using FluentValidation;

namespace Application.ProjectActivityTypes.Validators;

public class UpsertProjectActivityTypeRequestValidator : AbstractValidator<UpsertProjectActivityTypeRequest>
{
    public UpsertProjectActivityTypeRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Activity type name is required.")
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Activity type name is required.")
            .Must(name => name == name.Trim())
            .WithMessage("Activity type name must not start or end with whitespace.")
            .MaximumLength(100)
            .WithMessage("Activity type name must not exceed 100 characters.");

        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.Icon).MaximumLength(16);
        RuleFor(x => x.ColorKey).MaximumLength(30);
    }
}
