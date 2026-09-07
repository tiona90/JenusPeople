using Application.Core;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Departments.Commands;

public class DeleteDepartment
{
    public class Command : IRequest<Result<Unit>>
    {
        public int Id { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<Unit>>
    {
        public async Task<Result<Unit>> Handle(Command request, CancellationToken cancellationToken)
        {
            var department = await context.Departments.FindAsync([request.Id], cancellationToken);
            if (department is null)
                return Result<Unit>.Failure("Department not found.");

            // The department exists, so anything below is a conflict with its
            // current state, not a missing resource. Count the blockers so the
            // message tells the admin what to reassign rather than just "no".
            //
            // Every foreign key into Departments has to be counted here, and counted
            // past the global query filters, or the delete goes ahead and the
            // database refuses it — which surfaces as a 500, not an explanation.
            // That is exactly what happened on the deployed site while the same
            // delete succeeded locally: only the deployed database held the rows.
            // See Tests/WorkTrack.Tests/DeleteDepartmentBlockerTests.cs.
            //
            // IgnoreQueryFilters is the point of these two: a leaver's soft-deleted
            // profile and a soft-deleted project's assignment are both invisible to
            // an ordinary query while their foreign keys still hold. A department
            // reading "0 people" refusing to delete was this.
            var liveProfileCount = await context.EmployeeProfiles
                .CountAsync(ep => ep.DepartmentId == request.Id, cancellationToken);
            var allProfileCount = await context.EmployeeProfiles
                .IgnoreQueryFilters()
                .CountAsync(ep => ep.DepartmentId == request.Id, cancellationToken);
            var archivedProfileCount = allProfileCount - liveProfileCount;

            var managerCount = await context.UserDepartments
                .CountAsync(ud => ud.DepartmentId == request.Id, cancellationToken);

            // Cascading here would strip the department from its projects and could
            // leave one with none, which is a project nobody can see. Counted as a
            // blocker so the admin reassigns it deliberately instead.
            var liveProjectCount = await context.ProjectDepartments
                .CountAsync(pd => pd.DepartmentId == request.Id, cancellationToken);
            var allProjectCount = await context.ProjectDepartments
                .IgnoreQueryFilters()
                .CountAsync(pd => pd.DepartmentId == request.Id, cancellationToken);
            var archivedProjectCount = allProjectCount - liveProjectCount;

            // A timesheet keeps the department it was filed under, so it outlives its
            // author's move to another one — the department can read "0 people" and
            // still be referenced by years of history.
            var timesheetCount = await context.Timesheets
                .CountAsync(t => t.DepartmentId == request.Id, cancellationToken);
            var leaveCount = await context.AnnualLeaves
                .CountAsync(al => al.DepartmentId == request.Id, cancellationToken);

            var blockers = new List<string>();
            if (liveProfileCount > 0) blockers.Add(Count(liveProfileCount, "employee"));
            if (archivedProfileCount > 0) blockers.Add(Count(archivedProfileCount, "archived employee record"));
            if (managerCount > 0) blockers.Add(Count(managerCount, "assigned manager"));
            if (liveProjectCount > 0) blockers.Add(Count(liveProjectCount, "project"));
            if (archivedProjectCount > 0) blockers.Add(Count(archivedProjectCount, "archived project"));
            if (timesheetCount > 0) blockers.Add(Count(timesheetCount, "timesheet"));
            if (leaveCount > 0) blockers.Add(Count(leaveCount, "leave request"));

            if (blockers.Count > 0)
            {
                return Result<Unit>.Conflict(
                    $"Cannot delete \"{department.Name}\" — it still has {Join(blockers)}. "
                    + "Reassign or remove them first. Records kept for history, such as "
                    + "timesheets and leave requests, cannot be reassigned — deactivate "
                    + "the department instead of deleting it.");
            }

            context.Departments.Remove(department);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // The counts above are meant to catch every reference, but a foreign
                // key added later without a matching count would otherwise be a 500
                // again. Answer the same 409 rather than letting the next one through.
                return Result<Unit>.Conflict(
                    $"Cannot delete \"{department.Name}\" — something still references it. "
                    + "Deactivate the department instead of deleting it.");
            }

            return Result<Unit>.Success(Unit.Value);
        }

        private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

        /// <summary>"a", "a and b", "a, b and c" — the list can now reach seven.</summary>
        private static string Join(List<string> parts) => parts.Count switch
        {
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }
}
