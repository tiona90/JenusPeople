using Application.AdminUsers.DTOs;
using Application.AdminUsers.Support;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.AdminUsers.Commands;

public class ConfirmAdminUserEmail
{
    public class Command : IRequest<Result<AdminUserDto>>
    {
        public required string Id { get; set; }
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

            // Idempotent: confirming an already-confirmed address is a no-op that
            // still answers with the user, so the panel can refresh either way.
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;

                var updateResult = await userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    return Result<AdminUserDto>.ValidationFailure(
                        new Dictionary<string, string[]>
                        {
                            ["Identity"] = updateResult.Errors.Select(e => e.Description).ToArray(),
                        },
                        "Failed to mark email as verified.");
                }
            }

            var roles = await userManager.GetRolesAsync(user);
            return Result<AdminUserDto>.Success(AdminUserMapper.ToDto(user, roles));
        }
    }
}
