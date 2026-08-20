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

        // Moved from the handler: the current user must have an employee profile.
        RuleFor(x => x.RequestingUserId)
            .MustAsync(async (userId, cancellationToken) =>
                !string.IsNullOrEmpty(userId)
                && await context.EmployeeProfiles.AnyAsync(ep => ep.UserId == userId, cancellationToken))
            .WithMessage("No employee profile found for the current user.")
            .OverridePropertyName("EmployeeProfile");
    }
}
