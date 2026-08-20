using Application.Core;
using Application.Timesheets.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Queries;

/// <summary>
/// Reads one timesheet by id, through the same scope filter the list query uses.
/// Replaces an unscoped FirstOrDefaultAsync that handed any authenticated caller
/// any employee's timesheet — and their hours — for the price of guessing an id.
/// </summary>
public class GetTimesheetDetail
{
    public class Query : IRequest<Result<Domain.Timesheet>>
    {
        public required string Id { get; set; }
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<Domain.Timesheet>>
    {
        public async Task<Result<Domain.Timesheet>> Handle(Query request, CancellationToken cancellationToken)
        {
            IQueryable<Domain.Timesheet> query = context.Timesheets
                .Include(t => t.Entries)
                .AsNoTracking()
                .Where(t => t.Id == request.Id);

            var scoped = await TimesheetScope.ApplyAsync(
                context,
                query,
                request.RequestingUserId,
                request.IsAdmin,
                request.IsManager,
                cancellationToken);

            var timesheet = await scoped.FirstOrDefaultAsync(cancellationToken);

            // Out of scope is reported as "not found" on purpose: distinguishing it
            // from a genuinely missing id would confirm that another employee's
            // timesheet exists, which is most of what the caller wanted to learn.
            return timesheet is null
                ? Result<Domain.Timesheet>.Failure("Timesheet not found.")
                : Result<Domain.Timesheet>.Success(timesheet);
        }
    }
}
