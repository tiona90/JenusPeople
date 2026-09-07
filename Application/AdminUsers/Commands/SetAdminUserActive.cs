using Application.AdminUsers.DTOs;
using Application.AdminUsers.Support;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.AdminUsers.Commands;

/// <summary>
/// Switches an account on or off. The answer for someone who has left, in
/// preference to <see cref="DeleteAdminUser"/>: deleting them rewrites history
/// (every approval they gave is nulled out), while leaving them enabled leaves
/// a working set of credentials behind.
/// </summary>
public class SetAdminUserActive
{
    public class Command : IRequest<Result<AdminUserDto>>
    {
        public required string Id { get; set; }

        public required bool IsActive { get; set; }

        /// <summary>
        /// Who is asking. Deactivating your own account is refused for the same
        /// reason <see cref="DeleteAdminUser"/> refuses self-deletion: the last
        /// administrator switching themselves off locks everyone out of user
        /// administration.
        /// </summary>
        public string RequestingUserId { get; set; } = string.Empty;
    }

    public class Handler(UserManager<User> userManager) : IRequestHandler<Command, Result<AdminUserDto>>
    {
        public async Task<Result<AdminUserDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var user = await userManager.FindByIdAsync(request.Id);
            if (user is null)
            {
                return Result<AdminUserDto>.Failure("User not found.");
            }

            if (!request.IsActive && string.Equals(request.RequestingUserId, user.Id, StringComparison.Ordinal))
            {
                return Result<AdminUserDto>.Conflict("You cannot deactivate your own admin account.");
            }

            // Idempotent, like ConfirmAdminUserEmail: setting the state it is
            // already in answers with the user so the panel can refresh either
            // way, and — importantly — does not rotate the security stamp, which
            // would sign a working user out for no reason.
            if (user.IsActive != request.IsActive)
            {
                user.IsActive = request.IsActive;

                var updateResult = await userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    return IdentityFailure(request.IsActive
                        ? "Failed to activate the account."
                        : "Failed to deactivate the account.", updateResult);
                }

                if (!request.IsActive)
                {
                    // What actually ends the sessions the user already holds.
                    // Blocking the sign-in path alone would leave a deactivated
                    // employee working from this morning's cookie until it
                    // expired; rotating the stamp makes the cookie invalid at the
                    // next security-stamp revalidation (ValidationInterval in
                    // Program.cs).
                    var stampResult = await userManager.UpdateSecurityStampAsync(user);
                    if (!stampResult.Succeeded)
                    {
                        return IdentityFailure(
                            "The account was deactivated, but its existing sessions could not be ended.",
                            stampResult);
                    }
                }
            }

            var roles = await userManager.GetRolesAsync(user);
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
