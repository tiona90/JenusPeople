using Application.Timesheets.Commands;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Validators;

/// <summary>
/// Auto-registered (AddValidatorsFromAssemblyContaining) and executed by the
/// MediatR <c>ValidationBehavior</c> pipeline before <see cref="CreateTimesheet.Handler"/>.
/// </summary>
public class CreateTimesheetValidator : AbstractValidator<CreateTimesheet.Command>
{
    public const string WeekMessage = "A timesheet covers one week, Monday to Sunday.";

    public CreateTimesheetValidator(AppDbContext context)
    {
        RuleFor(x => x.RequestingUserId)
            .NotEmpty()
            .WithMessage("RequestingUserId is required.");

        RuleFor(x => x.PeriodStart)
            .NotEqual(default(DateTime))
            .WithMessage("PeriodStart is required.")
            .LessThanOrEqualTo(x => x.PeriodEnd)
            .WithMessage("PeriodStart must be on or before PeriodEnd.");

        RuleFor(x => x.PeriodEnd)
            .NotEqual(default(DateTime))
            .WithMessage("PeriodEnd is required.");

        // One Monday-to-Sunday week, as the editor and LogTaskTime build it. The
        // unique index is on PeriodStart alone, so a sheet starting on any other
        // day sat beside the Monday one for the same days without tripping it.
        RuleFor(x => x)
            .Must(x => x.PeriodStart.TimeOfDay == TimeSpan.Zero
                && x.PeriodStart.DayOfWeek == DayOfWeek.Monday
                && x.PeriodEnd.Date == x.PeriodStart.Date.AddDays(6))
            .When(x => x.PeriodStart != default && x.PeriodEnd != default)
            .WithMessage(WeekMessage)
            .OverridePropertyName(nameof(CreateTimesheet.Command.PeriodStart));

        // Moved from the handler: the current user must have an employee profile.
        RuleFor(x => x.RequestingUserId)
            .MustAsync(async (userId, cancellationToken) =>
                !string.IsNullOrEmpty(userId)
                && await context.EmployeeProfiles.AnyAsync(ep => ep.UserId == userId, cancellationToken))
            .WithMessage("No employee profile found for the current user.")
            .OverridePropertyName("EmployeeProfile");
    }
}
