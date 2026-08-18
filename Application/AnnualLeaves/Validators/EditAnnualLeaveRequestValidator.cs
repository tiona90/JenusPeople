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

            // Coverage (delegate) is optional; only validate it when one is nominated.
            When(x => x.AnnualLeave is not null && !string.IsNullOrWhiteSpace(x.AnnualLeave.DelegateId), () =>
            {
                RuleFor(x => x.AnnualLeave.DelegateId)
                    .MustAsync(async (delegateId, cancellationToken) =>
                        await context.Users.AnyAsync(u => u.Id == delegateId, cancellationToken))
                    .WithMessage("Selected delegate does not exist.");

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
                        && (al.Status == AnnualLeaveStatus.Pending || al.Status == AnnualLeaveStatus.Approved)
                        && al.StartDate.Date <= end
                        && al.EndDate.Date >= start,
                        cancellationToken);
                })
                .WithMessage("This request overlaps with an existing pending or approved leave request.");
        });
    }
}