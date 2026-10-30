using Application.AnnualLeaves.Commands;
using Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.AnnualLeaves.Validators;

public class EditAnnualLeaveRequestValidator : AbstractValidator<EditAnnualLeave.Command>
{
    public EditAnnualLeaveRequestValidator(Persistence.AppDbContext context)
    {
        RuleFor(x => x.AnnualLeave)
            .NotNull()
            .WithMessage("AnnualLeave payload is required.");

        When(x => x.AnnualLeave is not null, () =>
        {
            RuleFor(x => x.AnnualLeave)
                .SetValidator(new BaseAnnualLeaveValidator());

            RuleFor(x => x.AnnualLeave.Id)
                .NotEmpty()
                .WithMessage("Id is required.");

            RuleFor(x => x.AnnualLeave.LeaveTypeId)
                .MustAsync(async (leaveTypeId, cancellationToken) =>
                    await context.LeaveTypes.AnyAsync(lt => lt.Id == leaveTypeId && lt.IsActive, cancellationToken))
                .WithMessage("Selected leave type is invalid or inactive.");

            // Coverage is mandatory for an Employee or a Manager, read from the
            // stored leave's owner — the edit payload names no employee. An edit is a
            // full replace, so dropping the delegate here is refused the same as
            // never naming one on create.
            RuleFor(x => x.AnnualLeave)
                .CustomAsync(async (annualLeave, validationContext, cancellationToken) =>
                {
                    if (!string.IsNullOrWhiteSpace(annualLeave.DelegateId)) return;

                    var employeeId = await context.AnnualLeaves
                        .AsNoTracking()
                        .Where(al => al.Id == annualLeave.Id)
                        .Select(al => al.EmployeeId)
                        .FirstOrDefaultAsync(cancellationToken);

                    // A leave that matches nothing is the handler's "not found", not
                    // this rule's business.
                    if (employeeId is null) return;

                    var error = await CoverageRule.CheckAsync(
                        context, employeeId, annualLeave.DelegateId, cancellationToken);
                    if (error is not null) validationContext.AddFailure(nameof(annualLeave.DelegateId), error);
                });

            When(x => x.AnnualLeave is not null && !string.IsNullOrWhiteSpace(x.AnnualLeave.DelegateId), () =>
            {
                // A delegate the leave already names passes: the dialog sends it back
                // untouched, and somebody who has since moved department must not
                // stop a typo fix. A new one must be somebody the picker offers.
                RuleFor(x => x)
                    .MustAsync(async (command, cancellationToken) =>
                    {
                        var existing = await context.AnnualLeaves
                            .AsNoTracking()
                            .Where(al => al.Id == command.AnnualLeave.Id)
                            .Select(al => new { al.EmployeeId, al.DelegateId })
                            .FirstOrDefaultAsync(cancellationToken);
                        if (existing is null) return true;
                        if (existing.DelegateId == command.AnnualLeave.DelegateId) return true;
                        if (existing.EmployeeId == command.AnnualLeave.DelegateId) return true; // the self rule below says so
                        return await CoverageRule.IsEligibleDelegateAsync(
                            context, existing.EmployeeId, command.AnnualLeave.DelegateId!, cancellationToken);
                    })
                    .WithMessage(CoverageRule.DelegateNotEligibleMessage);

                RuleFor(x => x)
                    .MustAsync(async (command, cancellationToken) =>
                    {
                        var existing = await context.AnnualLeaves
                            .AsNoTracking()
                            .FirstOrDefaultAsync(al => al.Id == command.AnnualLeave.Id, cancellationToken);

                        return existing is null || existing.EmployeeId != command.AnnualLeave.DelegateId;
                    })
                    .WithMessage("You cannot nominate yourself to cover your own leave.");
            });

            RuleFor(x => x)
                .MustAsync(async (command, cancellationToken) =>
                {
                    var annualLeave = command.AnnualLeave;
                    if (string.IsNullOrWhiteSpace(annualLeave.Id)) return true;
                    if (annualLeave.StartDate == default || annualLeave.EndDate == default) return true;

                    var existing = await context.AnnualLeaves
                        .AsNoTracking()
                        .FirstOrDefaultAsync(al => al.Id == annualLeave.Id, cancellationToken);

                    if (existing is null) return true;

                    var start = annualLeave.StartDate.Date;
                    var end = annualLeave.EndDate.Date;

                    return !await context.AnnualLeaves.AnyAsync(al =>
                        al.Id != annualLeave.Id
                        && al.EmployeeId == existing.EmployeeId
                        && (al.Status == AnnualLeaveStatus.Pending || al.Status == AnnualLeaveStatus.AwaitingHrApproval || al.Status == AnnualLeaveStatus.Approved)
                        && al.StartDate.Date <= end
                        && al.EndDate.Date >= start,
                        cancellationToken);
                })
                .WithMessage("This request overlaps with an existing pending or approved leave request.");
        });
    }
}