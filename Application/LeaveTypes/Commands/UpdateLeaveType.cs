using Application.AnnualLeaves.Commands;
using Application.Core;
using Application.LeaveTypes.DTOs;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.LeaveTypes.Commands;

public class UpdateLeaveType
{
    public class Command : IRequest<Result<LeaveTypeDto>>
    {
        public int Id { get; set; }
        public required UpsertLeaveTypeRequest LeaveType { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<LeaveTypeDto>>
    {
        public async Task<Result<LeaveTypeDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var leaveType = await context.LeaveTypes.FindAsync([request.Id], cancellationToken);
            if (leaveType is null)
                return Result<LeaveTypeDto>.Failure("Leave type not found.");

            var wasRequiringManager = leaveType.RequiresManagerApproval;
            var wasRequiringHr = leaveType.RequiresHrApproval;
            var previousAllowance = leaveType.DefaultAllowance;
            var previouslyProRated = leaveType.ProRateFirstYear;

            /* A seeded type cannot be renamed — other code finds these by name, so a
               rename breaks it silently. Refused rather than quietly ignored: the edit
               dialog makes the field read-only, so anything reaching here with a
               different name is a caller going around the UI, and it should be told.
               Checked before the map, which would otherwise have already overwritten
               the stored name. Everything else about the type stays editable. */
            if (SystemLeaveTypes.IsSystem(leaveType.Name)
                && !string.Equals(leaveType.Name.Trim(), request.LeaveType.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Result<LeaveTypeDto>.Conflict(
                    $"{leaveType.Name} is a built-in leave type and cannot be renamed.");
            }

            mapper.Map(request.LeaveType, leaveType);

            var affectedProfiles = new Dictionary<string, EmployeeProfile>();

            /* Leave is configured once, for everyone: this allowance is the annual-leave
               budget, so moving it has to move every employee with it. Leaving profiles
               on their old AnnualLeaveEntitlement would have the screens quote the new
               figure while AnnualLeaveBalanceCalculator still enforces the old one. */
            var allowanceMoved = leaveType.AffectsBalance && leaveType.DefaultAllowance != previousAllowance;

            if (allowanceMoved && leaveType.DefaultAllowance <= 0)
            {
                // An entitlement of 0 switches the balance check off outright, so this
                // would quietly unpolice every request in the company at once.
                return Result<LeaveTypeDto>.Failure(
                    "The annual leave allowance must be at least 1 day — a 0 would remove the balance check for every employee.");
            }

            if (allowanceMoved)
            {
                var everyProfile = await context.EmployeeProfiles.ToListAsync(cancellationToken);
                foreach (var employeeProfile in everyProfile)
                {
                    employeeProfile.AnnualLeaveEntitlement = leaveType.DefaultAllowance;
                    // Picked up by the balance sync below, which recomputes what is left
                    // after the days each of them has already taken.
                    affectedProfiles[employeeProfile.Id] = employeeProfile;
                }
            }

            /* The pro-rating switch changes what this year's balance *is* for anyone
               who joined during it, without touching the stored entitlement — so the
               stored LeaveBalance has to be re-synced for everyone, the same way an
               allowance move does. Which profiles it actually changes depends on
               each start date, and the sync works that out. */
            var proRatingFlipped = leaveType.AffectsBalance && leaveType.ProRateFirstYear != previouslyProRated;
            if (proRatingFlipped && !allowanceMoved)
            {
                foreach (var employeeProfile in await context.EmployeeProfiles.ToListAsync(cancellationToken))
                    affectedProfiles[employeeProfile.Id] = employeeProfile;
            }

            /* Requests already in flight follow the switches. Every switch off:
               approve everything open, balance-checked, as the one-switch sweep
               always did. HR off with Manager still on: approve what was with HR —
               its manager stage is done. Manager off with HR on: Pending rows move
               to HR, since there is no manager stage left for them to clear. HR-only
               swapped for Manager-only in one save: the rows with HR never had a
               manager stage, so they go back to the manager rather than through.
               Switching anything *on* moves nothing: a Pending row will be advanced
               by the manager, and a row with HR stays with HR. */
            if (leaveType.IsActive)
            {
                var nowManager = leaveType.RequiresManagerApproval;
                var nowHr = leaveType.RequiresHrApproval;
                var everythingOff = !nowManager && !nowHr && (wasRequiringManager || wasRequiringHr);
                var hrDropped = wasRequiringHr && !nowHr && nowManager && wasRequiringManager;
                var managerDropped = wasRequiringManager && !nowManager && nowHr;
                // HR-only became Manager-only in one save: the rows with HR never had
                // a manager stage, and the save just turned one on, so they go to the
                // manager rather than through.
                var hrSwappedForManager = wasRequiringHr && !wasRequiringManager && !nowHr && nowManager;

                if (everythingOff || hrDropped)
                {
                    var toApprove = await context.AnnualLeaves
                        .Where(al => al.LeaveTypeId == leaveType.Id
                            && (al.Status == AnnualLeaveStatus.AwaitingHrApproval
                                || (everythingOff && al.Status == AnnualLeaveStatus.Pending)))
                        .ToListAsync(cancellationToken);

                    /* Every row clears what an ordinary approval clears: the
                       attachment policy, the pooled balance and the per-child ledger.
                       The two ledgers are read from the database, where nothing this
                       sweep has approved is saved yet, so what it has approved so far
                       is handed to them — otherwise one employee's several pending
                       requests each pass on their own and together overspend. Any
                       refusal refuses the whole save, as the balance check always has:
                       with every switch off, a row left behind would have nobody to
                       approve it. */
                    var sweptApproved = new List<AnnualLeave>();

                    foreach (var annualLeave in toApprove)
                    {
                        var attachmentError = AttachmentPolicyRule.Check(leaveType, annualLeave.EvidenceUrl);
                        if (attachmentError is not null)
                            return Result<LeaveTypeDto>.Conflict(attachmentError);

                        var employeeProfile = await context.EmployeeProfiles
                            .FirstOrDefaultAsync(ep => ep.Id == annualLeave.EmployeeProfileId, cancellationToken);

                        if (employeeProfile is not null)
                        {
                            var balanceError = await AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync(
                                context,
                                employeeProfile,
                                annualLeave,
                                excludeLeaveId: annualLeave.Id,
                                cancellationToken,
                                alsoApproved: sweptApproved);
                            if (balanceError is not null)
                                return Result<LeaveTypeDto>.Conflict(balanceError);

                            var perChildError = await PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync(
                                context,
                                annualLeave,
                                employeeProfile,
                                excludeLeaveId: annualLeave.Id,
                                cancellationToken,
                                alsoApproved: sweptApproved);
                            if (perChildError is not null)
                                return Result<LeaveTypeDto>.Conflict(perChildError);

                            affectedProfiles[employeeProfile.Id] = employeeProfile;
                        }

                        sweptApproved.Add(annualLeave);

                        var previous = annualLeave.Status;
                        annualLeave.Status = AnnualLeaveStatus.Approved;
                        annualLeave.ApprovedAt = DateTime.UtcNow;
                        annualLeave.ApprovedById = null;

                        context.LeaveStatusHistories.Add(new LeaveStatusHistory
                        {
                            Id = Guid.NewGuid().ToString(),
                            AnnualLeaveId = annualLeave.Id,
                            ChangedByUserId = annualLeave.EmployeeId,
                            OldStatus = previous,
                            NewStatus = AnnualLeaveStatus.Approved,
                            Comment = "Automatically approved based on leave type settings.",
                            ChangedAt = DateTime.UtcNow,
                        });
                    }
                }
                else if (managerDropped)
                {
                    var toHr = await context.AnnualLeaves
                        .Where(al => al.LeaveTypeId == leaveType.Id && al.Status == AnnualLeaveStatus.Pending)
                        .ToListAsync(cancellationToken);

                    foreach (var annualLeave in toHr)
                    {
                        annualLeave.Status = AnnualLeaveStatus.AwaitingHrApproval;
                        context.LeaveStatusHistories.Add(new LeaveStatusHistory
                        {
                            Id = Guid.NewGuid().ToString(),
                            AnnualLeaveId = annualLeave.Id,
                            ChangedByUserId = annualLeave.EmployeeId,
                            OldStatus = AnnualLeaveStatus.Pending,
                            NewStatus = AnnualLeaveStatus.AwaitingHrApproval,
                            Comment = "Moved to HR approval based on leave type settings.",
                            ChangedAt = DateTime.UtcNow,
                        });
                    }
                }
                else if (hrSwappedForManager)
                {
                    var toManager = await context.AnnualLeaves
                        .Where(al => al.LeaveTypeId == leaveType.Id && al.Status == AnnualLeaveStatus.AwaitingHrApproval)
                        .ToListAsync(cancellationToken);

                    foreach (var annualLeave in toManager)
                    {
                        annualLeave.Status = AnnualLeaveStatus.Pending;
                        context.LeaveStatusHistories.Add(new LeaveStatusHistory
                        {
                            Id = Guid.NewGuid().ToString(),
                            AnnualLeaveId = annualLeave.Id,
                            ChangedByUserId = annualLeave.EmployeeId,
                            OldStatus = AnnualLeaveStatus.AwaitingHrApproval,
                            NewStatus = AnnualLeaveStatus.Pending,
                            Comment = "Moved to manager approval based on leave type settings.",
                            ChangedAt = DateTime.UtcNow,
                        });
                    }
                }
            }

            // One transaction over both saves, as every leave handler that syncs a
            // balance has: the sync reads the swept approvals back out of the
            // database, so it must follow the first save, and a failure on the
            // second must not leave them approved against stale balances.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            await context.SaveChangesAsync(cancellationToken);

            foreach (var employeeProfile in affectedProfiles.Values)
            {
                await AnnualLeaveBalanceCalculator.SyncCurrentYearBalanceAsync(context, employeeProfile, cancellationToken);
            }

            if (affectedProfiles.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return Result<LeaveTypeDto>.Success(mapper.Map<LeaveTypeDto>(leaveType));
        }
    }
}
