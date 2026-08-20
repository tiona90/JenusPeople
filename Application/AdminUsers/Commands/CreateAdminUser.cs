using Application.AdminUsers.DTOs;
using Application.AdminUsers.Support;
using Application.Core;
using Domain;
using Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.AdminUsers.Commands;

public class CreateAdminUser
{
    public class Command : IRequest<Result<AdminUserDto>>
    {
        public required AdminCreateUserDto User { get; set; }
    }

    public class Handler(
        AppDbContext context,
        UserManager<User> userManager,
        IAccountEmailSender accountEmailSender,
        ILogger<Handler> logger) : IRequestHandler<Command, Result<AdminUserDto>>
    {
        public async Task<Result<AdminUserDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var email = request.User.Email.Trim();
            var displayName = request.User.DisplayName.Trim();

            // Shape and reference checks (department, manager, role names) are
            // CreateAdminUserValidator's job. What is left here is the one thing a
            // validator cannot settle without racing itself, plus whatever Identity
            // decides when it writes.
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                return Result<AdminUserDto>.Conflict("Email is already registered.");
            }

            var selectedRoles = NormalizeRoles(request.User.Roles);
            if (selectedRoles.Count == 0)
            {
                selectedRoles.Add(AppRoles.Employee);
            }

            var user = new User
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                PhoneNumber = string.IsNullOrWhiteSpace(request.User.PhoneNumber) ? null : request.User.PhoneNumber.Trim(),
                DateOfBirth = request.User.DateOfBirth,
                EmailConfirmed = true,
            };

            // No password on purpose. The account is activated by the welcome email
            // sent below, where the new user picks their own password — so an
            // administrator never chooses, sees, or has to relay one.
            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return IdentityFailure("Failed to create user.", createResult);
            }

            var entitlement = request.User.AnnualLeaveEntitlement ?? DefaultEntitlement;
            var employeeProfile = new EmployeeProfile
            {
                UserId = user.Id,
                DepartmentId = request.User.DepartmentId,
                ManagerId = string.IsNullOrWhiteSpace(request.User.ManagerId) ? null : request.User.ManagerId,
                JobTitle = string.IsNullOrWhiteSpace(request.User.JobTitle) ? null : request.User.JobTitle.Trim(),
                AnnualLeaveEntitlement = entitlement,
                LeaveBalance = entitlement,
            };

            context.EmployeeProfiles.Add(employeeProfile);
            await context.SaveChangesAsync(cancellationToken);

            var addRolesResult = await userManager.AddToRolesAsync(user, selectedRoles);
            if (!addRolesResult.Succeeded)
            {
                // Unwind rather than leave a user with no role: they would be able
                // to sign in and see nothing, which is harder to diagnose than a
                // failed create.
                await userManager.DeleteAsync(user);
                context.EmployeeProfiles.Remove(employeeProfile);
                await context.SaveChangesAsync(cancellationToken);

                return IdentityFailure("Failed to assign user roles.", addRolesResult);
            }

            var inviteEmailSent = await accountEmailSender.SendWelcomeInviteAsync(user, cancellationToken);
            if (!inviteEmailSent)
            {
                // Not worth failing the request over: the account exists and its
                // owner can still get in via "Forgot password?". Report it instead,
                // so the admin knows to tell them rather than waiting for an email
                // that never arrives.
                logger.LogWarning(
                    "Welcome invite email could not be sent to {Email} for new user {UserId}.",
                    user.Email,
                    user.Id);
            }

            var created = AdminUserMapper.ToDto(user, selectedRoles);
            created.InviteEmailSent = inviteEmailSent;

            return Result<AdminUserDto>.Success(created);
        }

        /// <summary>Entitlement granted when the admin leaves the field blank.</summary>
        public const int DefaultEntitlement = 20;

        /// <summary>
        /// Trimmed, de-duplicated, case-insensitively. Whether the names are real
        /// roles is checked by the validator; this only settles what was asked for.
        /// </summary>
        private static List<string> NormalizeRoles(IEnumerable<string>? roles) =>
            (roles ?? [])
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Identity reports its own reasons (password policy, duplicate user name),
        /// which the admin needs to see. Carried under "errors" as before, the shape
        /// the client already reads for these.
        /// </summary>
        private static Result<AdminUserDto> IdentityFailure(string message, IdentityResult result) =>
            Result<AdminUserDto>.ValidationFailure(
                new Dictionary<string, string[]>
                {
                    ["Identity"] = result.Errors.Select(e => e.Description).ToArray(),
                },
                message);
    }
}
