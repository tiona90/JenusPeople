using System;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

public class EditAnnualLeave
{
    public class Command : IRequest<Result<Unit>>
    {
        public required EditAnnualLeaveRequest AnnualLeave { get; set; }
        public string ChangedByUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }
    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var annualLeave = await context.AnnualLeaves
                .FindAsync([request.AnnualLeave.Id], cancellationToken);

            if (annualLeave is null)
                return Result<Unit>.Failure("Cannot find the annual leave.");

            if (string.IsNullOrWhiteSpace(request.ChangedByUserId))
            {
                return Result<Unit>.Failure("User context is required.");
            }

            var isInManagedDepartment = false;
            var isDirectReport = false;
            if (request.IsManager)
            {
                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.ChangedByUserId,
                    cancellationToken);

                isInManagedDepartment = annualLeave.DepartmentId.HasValue
                    && managerScope.ManagedDepartmentIds.Contains(annualLeave.DepartmentId.Value);
                isDirectReport = managerScope.DirectReportUserIds.Contains(annualLeave.EmployeeId);
            }

            var canEdit = request.IsAdmin || annualLeave.EmployeeId == request.ChangedByUserId;

            if (!canEdit && (isInManagedDepartment || isDirectReport))
            {
                canEdit = true;
            }

            if (!canEdit)
            {
                return Result<Unit>.Failure("You can only update your own leave requests or requests in your managed departments.");
            }

            if ((annualLeave.Status == AnnualLeaveStatus.Rejected || annualLeave.Status == AnnualLeaveStatus.Approved) && !request.IsAdmin)
            {
                return Result<Unit>.Failure("Approved and rejected leave requests cannot be edited.");
            }

            annualLeave.StartDate = request.AnnualLeave.StartDate;
            annualLeave.EndDate = request.AnnualLeave.EndDate;
            annualLeave.LeaveTypeId = request.AnnualLeave.LeaveTypeId;
            annualLeave.Reason = request.AnnualLeave.Reason;
            annualLeave.EvidenceUrl = request.AnnualLeave.EvidenceUrl;

            var employeeProfile = await context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.Id == annualLeave.EmployeeProfileId, cancellationToken);

            var canChangeStatus = request.IsAdmin || isInManagedDepartment || isDirectReport;
            if (request.AnnualLeave.Status.HasValue && !canChangeStatus)
            {
                return Result<Unit>.Failure("Only admins or managers of the request's department can change leave status.");
            }

            if (request.AnnualLeave.Status.HasValue && request.AnnualLeave.Status.Value != annualLeave.Status)
            {
                var changedByUserId = request.ChangedByUserId;
                var userExists = await context.Users
                    .AnyAsync(u => u.Id == changedByUserId, cancellationToken);
                if (!userExists)
                {
                    return Result<Unit>.Failure("Cannot resolve the user who changed status.");
                }

                var oldStatus = annualLeave.Status;
                var newStatus = request.AnnualLeave.Status.Value;
                annualLeave.Status = newStatus;


                if (newStatus == AnnualLeaveStatus.Approved)
                {
                    annualLeave.ApprovedAt = DateTime.UtcNow;
                    annualLeave.ApprovedById = changedByUserId;
                }
                else if (oldStatus == AnnualLeaveStatus.Approved)
                {
                    annualLeave.ApprovedAt = null;
                    annualLeave.ApprovedById = null;
                }

                context.LeaveStatusHistories.Add(new LeaveStatusHistory
                {
                    Id = Guid.NewGuid().ToString(),
                    AnnualLeaveId = annualLeave.Id,
                    ChangedByUserId = changedByUserId,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    Comment = request.AnnualLeave.StatusComment,
                    ChangedAt = DateTime.UtcNow
                });
            }

            if (employeeProfile is not null && annualLeave.Status == AnnualLeaveStatus.Approved)
            {
                var balanceError = await AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync(
                    context,
                    employeeProfile,
                    annualLeave,
                    excludeLeaveId: annualLeave.Id,
                    cancellationToken);
                if (balanceError is not null)
                    return Result<Unit>.Failure(balanceError);
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<Unit>.Failure(ConcurrencyError.Message);
            }

            if (employeeProfile is not null)
            {
                await AnnualLeaveBalanceCalculator.SyncCurrentYearBalanceAsync(context, employeeProfile, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
