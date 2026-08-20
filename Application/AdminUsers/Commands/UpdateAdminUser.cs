using Application.AdminUsers.DTOs;
using Application.AdminUsers.Support;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.AdminUsers.Commands;

public class UpdateAdminUser
{
    public class Command : IRequest<Result<AdminUserDto>>
    {
        public required string Id { get; set; }
        public required AdminUpdateUserDto User { get; set; }
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

            var email = request.User.Email.Trim();

            // A validator cannot settle this without racing itself, and the address
            // being taken is a conflict with existing data rather than a malformed
            // request — 409, per 8a0eda6.
            var existingByEmail = await userManager.FindByEmailAsync(email);
            if (existingByEmail is not null && existingByEmail.Id != user.Id)
            {
                return Result<AdminUserDto>.Conflict("Email is already registered by another user.");
            }

            // UserName tracks Email: login looks the account up by user name.
            user.Email = email;
            user.UserName = email;
            user.DisplayName = request.User.DisplayName;
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.User.PhoneNumber) ? null : request.User.PhoneNumber.Trim();
            user.DateOfBirth = request.User.DateOfBirth;

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return Result<AdminUserDto>.ValidationFailure(
                    new Dictionary<string, string[]>
                    {
                        ["Identity"] = updateResult.Errors.Select(e => e.Description).ToArray(),
                    },
                    "Failed to update user.");
            }

            var roles = await userManager.GetRolesAsync(user);
            return Result<AdminUserDto>.Success(AdminUserMapper.ToDto(user, roles));
        }
    }
}
