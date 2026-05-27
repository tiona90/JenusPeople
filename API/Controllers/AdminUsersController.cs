using Application.AdminUsers.DTOs;
using Domain;
using Microsoft.AspNetCore.Authorization;
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
    AppDbContext context) : BaseApiController
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
    public async Task<ActionResult<AdminUserDto>> CreateUser(AdminCreateUserDto request)
    {
        // Validate DepartmentId exists
        var departmentExists = await context.Departments.AnyAsync(d => d.Id == request.DepartmentId);
        if (!departmentExists)
        {
            return BadRequest(new { message = "Selected department does not exist." });
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Email and password are required." });
        }

        if (await userManager.FindByEmailAsync(request.Email) is not null)
        {
            return BadRequest(new { message = "Email is already registered." });
        }

        var selectedRoles = await ResolveRolesOrBadRequest(request.Roles);
        if (selectedRoles is null)
        {
            return BadRequest(new { message = "One or more roles are invalid." });
        }

        if (selectedRoles.Count == 0)
        {
            selectedRoles.Add(AppRoles.Employee);
        }

        var user = new User
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(new
            {
                message = "Failed to create user.",
                errors = createResult.Errors.Select(e => e.Description)
            });
        }

        // Create EmployeeProfile with DepartmentId
        var employeeProfile = new EmployeeProfile
        {
            UserId = user.Id,
            DepartmentId = request.DepartmentId,
            AnnualLeaveEntitlement = 20,
            LeaveBalance = 20,
        };
        context.EmployeeProfiles.Add(employeeProfile);
        await context.SaveChangesAsync();

        var addRolesResult = await userManager.AddToRolesAsync(user, selectedRoles);
        if (!addRolesResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            // Optionally remove EmployeeProfile if user creation fails
            context.EmployeeProfiles.Remove(employeeProfile);
            await context.SaveChangesAsync();
            return BadRequest(new
            {
                message = "Failed to assign user roles.",
                errors = addRolesResult.Errors.Select(e => e.Description)
            });
        }

        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, MapUser(user, selectedRoles));
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
            return BadRequest(new { message = "At least one role is required." });
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

    private static AdminUserDto MapUser(User user, IEnumerable<string> roles)
    {
        return new AdminUserDto
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            ImageUrl = user.ImageUrl ?? string.Empty,
            EmailConfirmed = user.EmailConfirmed,
            Roles = roles.OrderBy(r => r).ToList()
        };
    }

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
