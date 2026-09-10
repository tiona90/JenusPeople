using Application.LeaveTypes.DTOs;
using Domain;
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

        /* A per-child ledger belongs to Maternity and Paternity Leave and to nothing
           else. It is keyed by AnnualLeave.ChildId and a request against such a type
           must name a child, which only makes sense for the leave a birth grants —
           so it is not a flag any type may set. The edit dialog shows the three
           numbers for those two with no toggle and hides the section entirely for
           everything else; this is the server-side half of that. */
        RuleFor(x => x.PerChildEntitlement)
            .Must((request, perChild) =>
                !perChild || SystemLeaveTypes.SupportsPerChildEntitlement(request.Name))
            .WithMessage("Only Maternity Leave and Paternity Leave can carry a per-child entitlement.");

        /* Only when the type is per-child: otherwise these three describe nothing, and
           every leave type that is not per-child has them at 0. */
        When(x => x.PerChildEntitlement, () =>
        {
            RuleFor(x => x.PerChildTotalWeeks)
                .InclusiveBetween(1, 260)
                .WithMessage("Total per child must be between 1 and 260 weeks.");

            RuleFor(x => x.PerChildWeeksPerYear)
                .InclusiveBetween(1, 52)
                .WithMessage("The yearly cap must be between 1 and 52 weeks.")
                .LessThanOrEqualTo(x => x.PerChildTotalWeeks)
                .WithMessage("The yearly cap cannot exceed the total per child.");

            RuleFor(x => x.ChildEligibleUntilAge)
                .InclusiveBetween(1, 30)
                .WithMessage("Children must stop being eligible between ages 1 and 30.");

            // A per-child type keeps its own ledger. Counted in the pooled balance
            // as well, one day of leave would be charged twice.
            RuleFor(x => x.AffectsBalance)
                .Equal(false)
                .WithMessage("A per-child leave type keeps its own ledger and must not also affect the pooled balance.");
        });
    }
}
