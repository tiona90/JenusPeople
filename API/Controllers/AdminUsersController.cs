using API.Models;
using API.Services;
using Application.AdminUsers;
using Application.AdminUsers.Commands;
using Application.AdminUsers.DTOs;
using Domain;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Persistence;
using System.Security.Claims;
using Asp.Versioning;

namespace API.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[ApiVersion("1.0")]
public class AdminUsersController(
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    AppDbContext context,
    IAccountEmailSender accountEmailSender,
    ILogger<AdminUsersController> logger) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<List<AdminUserDto>>> GetUsers()
    {
        var users = userManager.Users
            .OrderBy(u => u.Email)
            .ToList();

        var result = new List<AdminUserDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            result.Add(MapUser(user, roles));
        }

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminUserDto>> GetUser(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(MapUser(user, roles));
    }

    [HttpPost]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> CreateUser(AdminCreateUserDto request)
    {
        var result = await Mediator.Send(
            new CreateAdminUser.Command { User = request },
            HttpContext.RequestAborted);

        // Not HandleResult on the success path: that answers 200, and this action
        // has always answered 201 with a Location header. The frontend only reads
        // the body, but a status change is not this migration's to make.
        if (result.IsSuccess && result.Value is not null)
        {
            return CreatedAtAction(nameof(GetUser), new { id = result.Value.Id }, result.Value);
        }

        return HandleResult(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AdminUserDto>> UpdateUser(string id, AdminUpdateUserDto request)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { message = "Email is required." });
        }

        var existingByEmail = await userManager.FindByEmailAsync(request.Email);
        if (existingByEmail is not null && existingByEmail.Id != user.Id)
        {
            return BadRequest(new { message = "Email is already registered by another user." });
        }

        user.Email = request.Email;
        user.UserName = request.Email;
        user.DisplayName = request.DisplayName;
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.DateOfBirth = request.DateOfBirth;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return BadRequest(new
            {
                message = "Failed to update user.",
                errors = updateResult.Errors.Select(e => e.Description)
            });
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(MapUser(user, roles));
    }

    [HttpPut("{id}/roles")]
    public async Task<ActionResult<AdminUserDto>> SetUserRoles(string id, AdminSetUserRolesDto request)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        var selectedRoles = await ResolveRolesOrBadRequest(request.Roles);
        if (selectedRoles is null)
        {
            return BadRequest(new { message = "One or more roles are invalid." });
        }

        if (selectedRoles.Count == 0)
        {
            return BadRequest(new { message = "A role is required." });
        }

        // One role per user. [MaxLength(1)] on the DTO already rejects a longer
        // list before the action runs, so this is a backstop that keeps the
        // invariant if that annotation is ever relaxed — role assignment decides
        // department scoping, so it is worth guarding twice.
        if (selectedRoles.Count > 1)
        {
            return BadRequest(new { message = "A user can have only one role." });
        }

        var currentRoles = await userManager.GetRolesAsync(user);

        var rolesToRemove = currentRoles.Except(selectedRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return BadRequest(new
                {
                    message = "Failed to remove existing roles.",
                    errors = removeResult.Errors.Select(e => e.Description)
                });
            }
        }

        var rolesToAdd = selectedRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToAdd.Length > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return BadRequest(new
                {
                    message = "Failed to add new roles.",
                    errors = addResult.Errors.Select(e => e.Description)
                });
            }
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(MapUser(user, roles));
    }

    [HttpPost("{id}/confirm-email")]
    public async Task<ActionResult<AdminUserDto>> ConfirmUserEmail(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return BadRequest(new
                {
                    message = "Failed to mark email as verified.",
                    errors = updateResult.Errors.Select(e => e.Description)
                });
            }
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(MapUser(user, roles));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteUser(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        var requestingUserId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (string.Equals(requestingUserId, user.Id, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "You cannot delete your own admin account." });
        }

        await using var transaction = await context.Database.BeginTransactionAsync(HttpContext.RequestAborted);

        await CleanupUserDependencies(user.Id, HttpContext.RequestAborted);

        var currentRoles = await userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            var removeRolesResult = await userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeRolesResult.Succeeded)
            {
                await transaction.RollbackAsync(HttpContext.RequestAborted);
                return BadRequest(new
                {
                    message = "Failed to remove user roles before deletion.",
                    errors = removeRolesResult.Errors.Select(e => e.Description)
                });
            }
        }

        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            await transaction.RollbackAsync(HttpContext.RequestAborted);
            return BadRequest(new
            {
                message = "Failed to delete user.",
                errors = deleteResult.Errors.Select(e => e.Description)
            });
        }

        await transaction.CommitAsync(HttpContext.RequestAborted);

        return NoContent();
    }

    private async Task CleanupUserDependencies(string userId, CancellationToken cancellationToken)
    {
        var userProfileId = await context.EmployeeProfiles
            .Where(ep => ep.UserId == userId)
            .Select(ep => ep.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(userProfileId))
        {
            var directReports = await context.EmployeeProfiles
                .Where(ep => ep.ManagerId == userProfileId)
                .ToListAsync(cancellationToken);

            foreach (var report in directReports)
            {
                report.ManagerId = null;
            }
        }

        var approvedLeaves = await context.AnnualLeaves
            .Where(al => al.ApprovedById == userId)
            .ToListAsync(cancellationToken);
        foreach (var leave in approvedLeaves)
        {
            leave.ApprovedById = null;
            leave.ApprovedAt = null;
        }

        // Null out DelegateId on other people's leave this user was covering.
        // That FK is Restrict too, so leaving it set fails the delete with a raw
        // DbUpdateException instead of the 400 the caller can act on.
        var delegatedLeaves = await context.AnnualLeaves
            .Where(al => al.DelegateId == userId)
            .ToListAsync(cancellationToken);
        foreach (var leave in delegatedLeaves)
        {
            leave.DelegateId = null;
        }

        var assignedByRows = await context.UserDepartments
            .Where(ud => ud.AssignedByUserId == userId)
            .ToListAsync(cancellationToken);
        foreach (var row in assignedByRows)
        {
            row.AssignedByUserId = null;
        }

        // Null out ApproverId on timesheets approved by this user
        var approvedTimesheets = await context.Timesheets
            .Where(t => t.ApproverId == userId)
            .ToListAsync(cancellationToken);
        foreach (var ts in approvedTimesheets)
        {
            ts.ApproverId = null;
            ts.ApprovedAt = null;
        }

        // Null out OwnerId on projects owned by this user
        var ownedProjects = await context.Projects
            .Where(p => p.OwnerId == userId)
            .ToListAsync(cancellationToken);
        foreach (var project in ownedProjects)
        {
            project.OwnerId = null;
        }

        // Delete timesheet status history rows changed by this user (on any timesheet)
        var timesheetStatusChangesByUser = await context.TimesheetStatusHistories
            .Where(h => h.ChangedByUserId == userId)
            .ToListAsync(cancellationToken);
        if (timesheetStatusChangesByUser.Count > 0)
        {
            context.TimesheetStatusHistories.RemoveRange(timesheetStatusChangesByUser);
        }

        // Delete the user's own timesheets (cascade deletes entries and status histories)
        if (!string.IsNullOrWhiteSpace(userProfileId))
        {
            var userTimesheets = await context.Timesheets
                .Where(t => t.EmployeeId == userProfileId)
                .ToListAsync(cancellationToken);
            if (userTimesheets.Count > 0)
            {
                context.Timesheets.RemoveRange(userTimesheets);
            }
        }

        var statusChangesByUser = await context.LeaveStatusHistories
            .Where(h => h.ChangedByUserId == userId)
            .ToListAsync(cancellationToken);
        if (statusChangesByUser.Count > 0)
        {
            context.LeaveStatusHistories.RemoveRange(statusChangesByUser);
        }

        var ownedUserDepartments = await context.UserDepartments
            .Where(ud => ud.UserId == userId)
            .ToListAsync(cancellationToken);
        if (ownedUserDepartments.Count > 0)
        {
            context.UserDepartments.RemoveRange(ownedUserDepartments);
        }

        var employeeLeaves = await context.AnnualLeaves
            .Where(al => al.EmployeeId == userId)
            .ToListAsync(cancellationToken);
        if (employeeLeaves.Count > 0)
        {
            context.AnnualLeaves.RemoveRange(employeeLeaves);
        }

        var profile = await context.EmployeeProfiles
            .FirstOrDefaultAsync(ep => ep.UserId == userId, cancellationToken);
        if (profile is not null)
        {
            context.EmployeeProfiles.Remove(profile);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    // Delegates to the shared mapper so the actions still handled here and the
    // ones already on MediatR cannot drift apart on response shape.
    private static AdminUserDto MapUser(User user, IEnumerable<string> roles) =>
        AdminUserMapper.ToDto(user, roles);

    private async Task<List<string>?> ResolveRolesOrBadRequest(IEnumerable<string>? roles)
    {
        var distinctRoles = (roles ?? Enumerable.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allRoles = await roleManager.Roles.Select(r => r.Name).ToListAsync();
        var existingRoleSet = new HashSet<string>(allRoles.Where(r => r is not null)!.Select(r => r!), StringComparer.OrdinalIgnoreCase);

        if (distinctRoles.Any(r => !existingRoleSet.Contains(r)))
        {
            return null;
        }

        return distinctRoles;
    }
}
