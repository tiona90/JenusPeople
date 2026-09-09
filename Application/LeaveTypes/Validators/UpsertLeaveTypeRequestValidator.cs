using Application.LeaveTypes.DTOs;
using FluentValidation;

namespace Application.LeaveTypes.Validators;

public class UpsertLeaveTypeRequestValidator : AbstractValidator<UpsertLeaveTypeRequest>
{
    public UpsertLeaveTypeRequestValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Leave type name is required.")
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Leave type name is required.")
            .Must(name => name == name.Trim())
            .WithMessage("Leave type name must not start or end with whitespace.")
            .MaximumLength(100)
            .WithMessage("Leave type name must not exceed 100 characters.");

        RuleFor(x => x.Icon).MaximumLength(16);
        RuleFor(x => x.ColorKey).MaximumLength(30);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.AllowanceUnit).MaximumLength(30);
        RuleFor(x => x.AccrualNotes).MaximumLength(250);
        RuleFor(x => x.EligibilityNotes).MaximumLength(250);

        RuleFor(x => x.DefaultAllowance).InclusiveBetween(0, 365);
        RuleFor(x => x.MaxCarryoverDays)
            .InclusiveBetween(0, 365)
            .WithMessage("Max carryover days must be between 0 and 365.");
        RuleFor(x => x.MinNoticeDays).InclusiveBetween(0, 365);
        RuleFor(x => x.MaxConsecutiveDays).InclusiveBetween(0, 365);
    }
}
