using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Commands;

/// <summary>
/// One employee cannot hold two open or approved leaves over the same day. The
/// create and edit validators ask it of the request being saved; a status change
/// has to ask it too when it reopens a request — a Rejected one counts for no
/// overlap, so its dates may since have been filed and approved again.
/// </summary>
public static class LeaveOverlapRule
{
    public const string Message = "This request overlaps with an existing pending or approved leave request.";

    public static Task<bool> OverlapsAsync(
        AppDbContext context, AnnualLeave annualLeave, CancellationToken cancellationToken)
    {
        var start = annualLeave.StartDate.Date;
        var end = annualLeave.EndDate.Date;
        return context.AnnualLeaves.AsNoTracking().AnyAsync(al =>
            al.Id != annualLeave.Id
            && al.EmployeeId == annualLeave.EmployeeId
            && (al.Status == AnnualLeaveStatus.Pending || al.Status == AnnualLeaveStatus.AwaitingHrApproval || al.Status == AnnualLeaveStatus.Approved)
            && al.StartDate.Date <= end
            && al.EndDate.Date >= start,
            cancellationToken);
    }
}
