using Application.AnnualLeaves.Commands;
using Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Validators;

public class CreateAnnualLeaveRequestValidator : AbstractValidator<CreateAnnualLeave.Command>
{
    public CreateAnnualLeaveRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.AnnualLeave)
            .NotNull()
            .WithMessage("AnnualLeave payload is required.");

        When(x => x.AnnualLeave is not null, () =>
        {
            RuleFor(x => x.AnnualLeave)
                .SetValidator(new BaseAnnualLeaveValidator());

            RuleFor(x => x.AnnualLeave.EmployeeId)
                .NotEmpty()
                .WithMessage("EmployeeId is required.")
                .MustAsync(async (employeeId, cancellationToken) =>
                    await context.Users.AnyAsync(u => u.Id == employeeId, cancellationToken))
                .WithMessage("Employee does not exist.");

            RuleFor(x => x.AnnualLeave.EmployeeId)
                .MustAsync(async (employeeId, cancellationToken) =>
                    await context.EmployeeProfiles.AnyAsync(ep => ep.UserId == employeeId, cancellationToken))
                .WithMessage("Employee profile does not exist.");

            RuleFor(x => x.AnnualLeave.LeaveTypeId)
                .MustAsync(async (leaveTypeId, cancellationToken) =>
                    await context.LeaveTypes.AnyAsync(lt => lt.Id == leaveTypeId && lt.IsActive, cancellationToken))
                .WithMessage("Selected leave type is invalid or inactive.");

            RuleFor(x => x)
                .MustAsync(async (command, cancellationToken) =>
                {
                    var annualLeave = command.AnnualLeave;
                    if (string.IsNullOrWhiteSpace(annualLeave.EmployeeId)) return true;
                    if (annualLeave.StartDate == default || annualLeave.EndDate == default) return true;

                    var start = annualLeave.StartDate.Date;
                    var end = annualLeave.EndDate.Date;

                    return !await context.AnnualLeaves.AnyAsync(al =>
                        al.EmployeeId == annualLeave.EmployeeId
                        && (al.Status == AnnualLeaveStatus.Pending || al.Status == AnnualLeaveStatus.Approved)
                        && al.StartDate.Date <= end
                        && al.EndDate.Date >= start,
                        cancellationToken);
                })
                .WithMessage("This request overlaps with an existing pending or approved leave request.");
        });
    }
}