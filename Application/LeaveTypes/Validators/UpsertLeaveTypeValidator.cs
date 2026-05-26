using Application.LeaveTypes.DTOs;
using FluentValidation;

namespace Application.LeaveTypes.Validators;

public class UpsertLeaveTypeValidator : AbstractValidator<UpsertLeaveTypeRequest>
{
    public UpsertLeaveTypeValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Leave type name is required.")
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Leave type name is required.")
            .MaximumLength(100)
            .WithMessage("Leave type name must not exceed 100 characters.");

        RuleFor(x => x.Icon).MaximumLength(16);
        RuleFor(x => x.ColorKey).MaximumLength(30);
        RuleFor(x => x.Description).MaximumLength(300);
        RuleFor(x => x.AllowanceUnit).MaximumLength(30);
        RuleFor(x => x.AccrualNotes).MaximumLength(250);
        RuleFor(x => x.EligibilityNotes).MaximumLength(250);

        RuleFor(x => x.DefaultAllowance).InclusiveBetween(0, 365);
        RuleFor(x => x.MinNoticeDays).InclusiveBetween(0, 365);
        RuleFor(x => x.MaxConsecutiveDays).InclusiveBetween(0, 365);
    }
}
