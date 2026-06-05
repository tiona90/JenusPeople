using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Settings.Commands;

// Danger-zone action: delete leave & timesheet approval-history records from
// the past 30 days. Returns the number of records removed.
public class ClearApprovalHistory
{
    public class Command : IRequest<Result<int>> { }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<int>>
    {
        public async Task<Result<int>> Handle(Command request, CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);

            var leaveDeleted = await context.LeaveStatusHistories
                .Where(h => h.ChangedAt >= cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var timesheetDeleted = await context.TimesheetStatusHistories
                .Where(h => h.ChangedAt >= cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            return Result<int>.Success(leaveDeleted + timesheetDeleted);
        }
    }
}
