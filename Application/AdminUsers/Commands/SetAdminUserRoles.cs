using Application.AdminUsers.DTOs;
using Application.AdminUsers.Support;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AdminUsers.Commands;

public class SetAdminUserRoles
{
    public const string LastSystemAdministratorMessage =
        "This is the only active System Administrator. Give the role to someone else first.";

    public class Command : IRequest<Result<AdminUserDto>>
    {
        public required string Id { get; set; }
        public required AdminSetUserRolesDto Roles { get; set; }
    }

    public class Handler(UserManager<User> userManager, AppDbContext context)
        : IRequestHandler<Command, Result<AdminUserDto>>
    {
        public async Task<Result<AdminUserDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await userManager.FindByIdAsync(request.Id);
            if (user is null)
            {
                return Result<AdminUserDto>.Failure("User not found.");
            }

            // Which names are real roles, how many are allowed, and that at least one
            // was asked for are all SetAdminUserRolesValidator's job.
            var selectedRoles = (request.Roles.Roles ?? [])
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var currentRoles = await userManager.GetRolesAsync(user);

            var rolesToRemove = currentRoles.Except(selectedRoles, StringComparer.OrdinalIgnoreCase).ToArray();

            // The workspace always keeps an active System Administrator: without one
            // nobody can manage users or settings, and on the deployed host — where
            // the seeder does not run — only a hand edit of the database gets it back.
            if (rolesToRemove.Contains(AppRoles.SystemAdministrator, StringComparer.OrdinalIgnoreCase))
            {
                var others = await userManager.GetUsersInRoleAsync(AppRoles.SystemAdministrator);
                if (!others.Any(other => other.Id != user.Id && other.IsActive))
                    return Result<AdminUserDto>.Conflict(LastSystemAdministratorMessage);
            }
            if (rolesToRemove.Length > 0)
            {
                var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    return IdentityFailure("Failed to remove existing roles.", removeResult);
                }
            }

            var rolesToAdd = selectedRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
            if (rolesToAdd.Length > 0)
            {
                var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    return IdentityFailure("Failed to add new roles.", addResult);
                }
            }

            var roles = await userManager.GetRolesAsync(user);

            // A UserDepartment row is a department this person covers beyond their
            // own profile: an extra one for a Manager, the whole scope for an HR
            // Administrator. The two are not the same answer — a team someone runs is
            // not a reach a System Administrator granted — so the rows go with the
            // role that gave them meaning: **any** change of role clears the set,
            // whether or not the new role could hold one. Only a save that leaves the
            // role as it was keeps them. The admin dialog re-supplies an HR
            // Administrator's departments in the same save, immediately after this
            // call, so a promotion into HR still ends up with the set that was chosen
            // for it — rather than inheriting a manager's old team unasked.
            var rolesChanged = !currentRoles.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(selectedRoles);

            if (rolesChanged)
            {
                var assignments = await context.UserDepartments
                    .Where(ud => ud.UserId == user.Id)
                    .ToListAsync(cancellationToken);

                if (assignments.Count > 0)
                {
                    context.UserDepartments.RemoveRange(assignments);
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            return Result<AdminUserDto>.Success(AdminUserMapper.ToDto(user, roles));
        }

        private static Result<AdminUserDto> IdentityFailure(string message, IdentityResult result) =>
            Result<AdminUserDto>.ValidationFailure(
                new Dictionary<string, string[]>
                {
                    ["Identity"] = result.Errors.Select(e => e.Description).ToArray(),
                },
                message);
    }
}
