using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace API.Hubs;

[Authorize]
public class NotificationsHub : Hub
{
    public const string AdminGroup = "role:Admin";

    public static string DepartmentManagerGroup(int departmentId) => $"dept-mgr:{departmentId}";

    private readonly UserManager<User> _userManager;
    private readonly AppDbContext _context;

    public NotificationsHub(UserManager<User> userManager, AppDbContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    // When a client connects, register them into the audience groups whose
    // events they're allowed to receive. Connection→group membership is
    // ephemeral; SignalR removes the connection from all groups on disconnect.
    public override async Task OnConnectedAsync()
    {
        var principal = Context.User;
        if (principal is null)
        {
            await base.OnConnectedAsync();
            return;
        }

        var user = await _userManager.GetUserAsync(principal);
        if (user is null)
        {
            await base.OnConnectedAsync();
            return;
        }

        var roles = await _userManager.GetRolesAsync(user);

        if (roles.Contains(AppRoles.Admin))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
        }

        if (roles.Contains(AppRoles.Manager))
        {
            // Manager's audience scope follows the existing visibility model:
            // the department(s) attached to the manager's own EmployeeProfile.
            var managedDeptIds = await _context.EmployeeProfiles
                .Where(ep => ep.UserId == user.Id)
                .Select(ep => ep.DepartmentId)
                .Distinct()
                .ToListAsync();

            foreach (var deptId in managedDeptIds)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, DepartmentManagerGroup(deptId));
            }
        }

        await base.OnConnectedAsync();
    }
}
