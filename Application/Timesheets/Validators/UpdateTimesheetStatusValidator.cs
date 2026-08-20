using Application.Timesheets.Commands;
using Domain;
using FluentValidation;

namespace Application.Timesheets.Validators;

/// <summary>
/// Auto-registered and executed by the MediatR <c>ValidationBehavior</c> pipeline
/// before <see cref="UpdateTimesheetStatus.Handler"/>. Holds the input rules moved
/// out of the handler (valid target status + comment-required-on-reject); the
/// manager/department authorization remains in the handler.
/// </summary>
public class UpdateTimesheetStatusValidator : AbstractValidator<UpdateTimesheetStatus.Command>
{
    public UpdateTimesheetStatusValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Timesheet Id is required.");

        RuleFor(x => x.RequestingUserId)
            .NotEmpty()
            .WithMessage("RequestingUserId is required.");

        RuleFor(x => x.NewStatus)
            .Must(status => status is TimesheetStatus.Approved or TimesheetStatus.Rejected)
            .WithMessage("Only Approved or Rejected transitions are supported by this command.");

        RuleFor(x => x.Comment)
            .NotEmpty()
            .When(x => x.NewStatus == TimesheetStatus.Rejected)
            .WithMessage("A reason is required when rejecting a timesheet.");
    }
}
