using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.TaskSettings.Commands;

/// <summary>
/// The System Administrator's save of the Task Settings — a full replace. Switching
/// confirmation off closes every task waiting for it in the same save (Done, no
/// confirmer, the send-back note cleared) and emails nobody, the way UpdateLeaveType
/// sweeps leave in flight: a task must not wait on a step that no longer exists.
/// Switching it on moves nothing.
/// </summary>
public class UpdateWorkTaskSettings
{
    public class Command : IRequest<Result<WorkTaskSettingsDto>>
    {
        public WorkTaskSettingsDto Settings { get; set; } = new();

        /// <summary>Test seam for the sweep's completion time; the controller leaves it null.</summary>
        public DateTime? NowUtc { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<WorkTaskSettingsDto>>
    {
        public async Task<Result<WorkTaskSettingsDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var row = await context.WorkTaskSettings.FirstOrDefaultAsync(cancellationToken);
            if (row is null)
            {
                row = new Domain.WorkTaskSettings();
                context.WorkTaskSettings.Add(row);
            }

            var confirmationDropped = row.RequireCompletionConfirmation && !request.Settings.RequireCompletionConfirmation;
            request.Settings.ApplyTo(row);

            if (confirmationDropped)
            {
                var now = request.NowUtc ?? DateTime.UtcNow;
                var waiting = await context.WorkTasks
                    .Where(t => t.Status == WorkTaskStatus.AwaitingConfirmation)
                    .ToListAsync(cancellationToken);
                foreach (var task in waiting)
                {
                    task.Status = WorkTaskStatus.Done;
                    task.CompletedAtUtc = now;
                    task.ConfirmedById = null;
                    task.SentBackReason = null;
                    task.SentBackAtUtc = null;
                    task.UpdatedAtUtc = now;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            return Result<WorkTaskSettingsDto>.Success(WorkTaskSettingsDto.From(row));
        }
    }
}
