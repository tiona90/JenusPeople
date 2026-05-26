using Application.AnnualLeaves.DTOs;
using FluentValidation;

namespace Application.AnnualLeaves.Validators;

public class BaseAnnualLeaveValidator : AbstractValidator<BaseAnnualLeaveDto>
{
    public BaseAnnualLeaveValidator()
    {
        RuleFor(x => x.StartDate)
            .NotEqual(default(DateTime))
            .WithMessage("StartDate is required.")
            .LessThanOrEqualTo(x => x.EndDate)
            .WithMessage("StartDate must be on or before EndDate.");

        RuleFor(x => x.EndDate)
            .NotEqual(default(DateTime))
            .WithMessage("EndDate is required.")
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Reason is required.")
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage("Reason is required.")
            .MaximumLength(500)
            .WithMessage("Reason must not exceed 500 characters.");

        RuleFor(x => x)
            .Must(x => x.EndDate.Date.Subtract(x.StartDate.Date).TotalDays <= 365)
            .WithMessage("Leave request cannot exceed 365 calendar days.");
    }
}