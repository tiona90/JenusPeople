using Application.Departments.DTOs;
using FluentValidation;

namespace Application.Departments.Validators;

/// <summary>
/// Shape rules for the create/update payload, mirroring the DataAnnotations on
/// <see cref="UpsertDepartmentRequest"/> so a request reaching the handler through
/// MediatR is held to the same limits as one bound by MVC.
///
/// Nothing here duplicates the handlers' uniqueness check: that one belongs where
/// it is, since a duplicate code is a conflict with existing data (409) rather
/// than a malformed request (400).
/// </summary>
public class UpsertDepartmentRequestValidator : AbstractValidator<UpsertDepartmentRequest>
{
    public UpsertDepartmentRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Department name is required.")
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Department name is required.")
            .MaximumLength(100)
            .WithMessage("Department name must not exceed 100 characters.");

        RuleFor(x => x.Code)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Department code is required.")
            .Must(code => !string.IsNullOrWhiteSpace(code))
            .WithMessage("Department code is required.")
            .MaximumLength(10)
            .WithMessage("Department code must not exceed 10 characters.");
    }
}
