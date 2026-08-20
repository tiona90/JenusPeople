using Application.Settings.Commands;
using Application.Settings.Support;
using FluentValidation;

namespace Application.Settings.Validators;

/// <summary>
/// The handler checked all of this inline and reported failures with
/// Result.Failure, which maps to 404 — so "leave year start month must be between
/// 1 and 12" reached the admin as Not Found, with no indication of which field was
/// wrong. These are the same rules, in the layer the rest of the app puts them in,
/// answering 400 with the offending field named.
///
/// No rule here is new. The handler keeps its own copy of the checks it needs to
/// produce normalised values, sharing <see cref="WorkingTimeFormat"/> so the two
/// cannot disagree about what a valid time or day name is.
/// </summary>
public class UpdateAppSettingsValidator : AbstractValidator<UpdateAppSettings.Command>
{
    public UpdateAppSettingsValidator()
    {
        RuleFor(x => x.LeaveYearStartMonth)
            .InclusiveBetween(1, 12)
            .WithMessage("Leave year start month must be between 1 and 12.");

        RuleFor(x => x.MaxCarryoverDays)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Max carryover days cannot be negative.");

        RuleFor(x => x.DefaultAnnualEntitlement)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Default annual entitlement must be at least 1.");

        RuleFor(x => x.FinancialYearStartMonth)
            .InclusiveBetween(1, 12)
            .WithMessage("Financial year start month must be between 1 and 12.");

        RuleFor(x => x.WorkingHoursStart)
            .Must(value => WorkingTimeFormat.TryNormalizeTime(value, out _))
            .WithMessage("Working hours start must be a valid time (HH:mm).");

        RuleFor(x => x.WorkingHoursEnd)
            .Must(value => WorkingTimeFormat.TryNormalizeTime(value, out _))
            .WithMessage("Working hours end must be a valid time (HH:mm).");

        RuleFor(x => x.WeeklyHoursTarget)
            .InclusiveBetween(1, 168)
            .WithMessage("Weekly hours target must be between 1 and 168.");

        RuleFor(x => x.TimesheetSubmissionDeadlineDay)
            .Must(WorkingTimeFormat.IsKnownDay)
            .WithMessage("Timesheet submission deadline day must be a weekday (mon–sun).");

        RuleFor(x => x.TimesheetSubmissionDeadlineTime)
            .Must(value => WorkingTimeFormat.TryNormalizeTime(value, out _))
            .WithMessage("Timesheet submission deadline time must be a valid time (HH:mm).");

        // Only meaningful for the custom schedule: the other WorkingDays values
        // carry their own days, so an unused custom list is not an error.
        RuleFor(x => x.WorkingDaysCustom)
            .Must(value => WorkingTimeFormat.NormalizeWorkingDaysCustom(value).Length > 0)
            .WithMessage("Select at least one working day for the custom schedule.")
            .When(x => string.Equals(x.WorkingDays?.Trim(), "custom", StringComparison.OrdinalIgnoreCase));
    }
}
