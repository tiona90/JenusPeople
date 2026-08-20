using Application.AdminUsers.DTOs;
using Application.AdminUsers.Support;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.AdminUsers.Queries;

public class GetAdminUserDetail
{
    public class Query : IRequest<Result<AdminUserDto>>
    {
        public required string Id { get; set; }
    }

    public class Handler(UserManager<User> userManager) : IRequestHandler<Query, Result<AdminUserDto>>
    {
        public async Task<Result<AdminUserDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var user = await userManager.FindByIdAsync(request.Id);
            if (user is null)
            {
                // Keeps the message the controller returned, which the admin panel
                // surfaces verbatim through getApiErrorMessage.
                return Result<AdminUserDto>.Failure("User not found.");
            }

            var roles = await userManager.GetRolesAsync(user);
            return Result<AdminUserDto>.Success(AdminUserMapper.ToDto(user, roles));
        }
    }
}
