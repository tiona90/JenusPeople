using Application.AdminUsers.DTOs;
using Application.AdminUsers.Support;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.AdminUsers.Queries;

public class GetAdminUserList
{
    public class Query : IRequest<List<AdminUserDto>> { }

    public class Handler(UserManager<User> userManager) : IRequestHandler<Query, List<AdminUserDto>>
    {
        public async Task<List<AdminUserDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            // Awaited now: the controller blocked a request thread on ToList() here.
            var users = await userManager.Users
                .OrderBy(u => u.Email)
                .ToListAsync(cancellationToken);

            var result = new List<AdminUserDto>(users.Count);
            foreach (var user in users)
            {
                // One roles query per user, as before. Worth collapsing into a join,
                // but that is a behaviour question for its own change, not something
                // to slip into a migration.
                var roles = await userManager.GetRolesAsync(user);
                result.Add(AdminUserMapper.ToDto(user, roles));
            }

            return result;
        }
    }
}
