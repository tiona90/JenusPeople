using Application.Core;
using Application.Timesheets.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Commands;

/// <summary>
/// Deletes a Draft timesheet.
///
/// Authorization goes through <see cref="TimesheetAccess.AuthorizeWriteAsync"/>,
/// which is the same rule the timesheet-entry actions already enforce. The rule
/// this replaced was applied inline in the controller and recognised only the
/// employee the timesheet belongs to — so an Admin could not delete a timesheet at
/// all, nor could the manager responsible for it, even though both may add, edit
/// and delete its entries. A caller who could empty a timesheet one entry at a
/// time could not remove the timesheet itself.
///
/// The order matters as much as the rule. The inline version answered "only Draft
/// timesheets can be deleted" before it had established who was asking, so a
/// caller with no claim on a timesheet still learned that it existed and what
/// state it was in. Authorization now runs first and the status rule second.
/// </summary>
public class DeleteTimesheet
{
    public class Command : IRequest<Result<Unit>>
    {
        public required string Id { get; set; }
        public required string RequestingUserId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var access = await TimesheetAccess.AuthorizeWriteAsync(
                context,
                request.Id,
                request.RequestingUserId,
                request.IsAdmin,
                request.IsManager,
                cancellationToken);

            if (!access.IsSuccess)
            {
                return Deny(access);
            }

            var timesheet = access.Value!;

            // Checked after authorization, not before: this message describes the
            // timesheet, so only someone entitled to see it should get it.
            if (timesheet.Status != TimesheetStatus.Draft)
            {
                return Result<Unit>.Conflict("Only Draft timesheets can be deleted.");
            }

            // TimesheetEntry cascades from Timesheet in the schema, so the database
            // clears the entries on its own. Pulling them into the change tracker
            // first is what makes the delete behave the same way on the in-memory
            // provider, which enforces no foreign keys and would otherwise leave
            // them orphaned.
            await context.Entry(timesheet)
                .Collection(t => t.Entries)
                .LoadAsync(cancellationToken);

            context.Timesheets.Remove(timesheet);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<Unit>.Failure(ConcurrencyError.Message);
            }

            return Result<Unit>.Success(Unit.Value);
        }

        /// <summary>
        /// Carries a refusal from <see cref="TimesheetAccess"/> across to
        /// <c>Result&lt;Unit&gt;</c> with its <see cref="ResultErrorKind"/> intact.
        /// Rebuilding it with <c>Failure</c> would reset the kind to NotFound and
        /// turn "not yours" back into "no such timesheet".
        /// </summary>
        private static Result<Unit> Deny(Result<Timesheet> access) => new()
        {
            IsSuccess = false,
            Error = access.Error,
            ErrorKind = access.ErrorKind,
        };
    }
}
