using Application.Children.DTOs;
using FluentValidation;

namespace Application.Children.Validators;

public class UpsertChildRequestValidator : AbstractValidator<UpsertChildRequest>
{
    public UpsertChildRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("The child's name is required.")
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("The child's name is required.")
            .MaximumLength(100)
            .WithMessage("The child's name must not exceed 100 characters.");

        RuleFor(x => x.DateOfBirth)
            .NotEqual(default(DateOnly))
            .WithMessage("The child's date of birth is required.")
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("The child's date of birth cannot be in the future.")
            // Bounds the eligibility arithmetic and catches a mistyped year, which
            // would otherwise read as a child who is 1900 years old.
            .GreaterThan(new DateOnly(1900, 1, 1))
            .WithMessage("Check the child's date of birth.");
    }
}
