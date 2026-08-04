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
            var profileCount = await context.EmployeeProfiles
                .CountAsync(ep => ep.DepartmentId == request.Id, cancellationToken);
            var managerCount = await context.UserDepartments
                .CountAsync(ud => ud.DepartmentId == request.Id, cancellationToken);

            if (profileCount > 0 || managerCount > 0)
            {
                var blockers = new List<string>();
                if (profileCount > 0) blockers.Add($"{profileCount} employee{(profileCount == 1 ? "" : "s")}");
                if (managerCount > 0) blockers.Add($"{managerCount} assigned manager{(managerCount == 1 ? "" : "s")}");

                return Result<Unit>.Conflict(
                    $"Cannot delete \"{department.Name}\" — it still has {string.Join(" and ", blockers)}. "
                    + "Reassign them to another department first.");
            }

            context.Departments.Remove(department);
            await context.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
    }
}
