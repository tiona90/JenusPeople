using FluentValidation;

namespace Application.Timesheets.Commands;

/// <summary>
/// Auto-registered and executed by the MediatR <c>ValidationBehavior</c> pipeline
/// before <see cref="SubmitTimesheet.Handler"/>. Validates the request inputs;
/// resource-level authorization (does the caller own the timesheet) stays in the
/// handler since it requires the loaded timesheet.
/// </summary>
public class SubmitTimesheetValidator : AbstractValidator<SubmitTimesheet.Command>
{
    public SubmitTimesheetValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Timesheet Id is required.");

        RuleFor(x => x.RequestingUserId)
            .NotEmpty()
            .WithMessage("RequestingUserId is required.");
    }
}
