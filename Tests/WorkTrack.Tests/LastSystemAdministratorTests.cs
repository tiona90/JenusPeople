using Application.AdminUsers.Commands;
using Application.AdminUsers.DTOs;
using Application.Core;
using Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The workspace always keeps an active System Administrator. Nothing stopped the
/// last one changing their own role, after which nobody could manage users,
/// departments or settings — and on the deployed host, where the seeder does not
/// run, the only way back was editing the database by hand.
/// </summary>
public class LastSystemAdministratorTests : IDisposable
{
    private readonly ServiceProvider _services;

    public LastSystemAdministratorTests()
    {
        var collection = new ServiceCollection();
        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"last-sysadmin-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        collection.AddIdentityCore<User>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>();
        _services = collection.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();
    private UserManager<User> Users => _services.GetRequiredService<UserManager<User>>();
    private RoleManager<Role> Roles => _services.GetRequiredService<RoleManager<Role>>();

    private async Task<User> GivenAsync(string email, string role, bool active = true)
    {
        var user = new User { DisplayName = email, UserName = email, Email = email, EmailConfirmed = true, IsActive = active };
        Assert.True((await Users.CreateAsync(user, "Pa$$w0rd")).Succeeded);
        if (!await Roles.RoleExistsAsync(role))
            Assert.True((await Roles.CreateAsync(new Role { Name = role })).Succeeded);
        Assert.True((await Users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private async Task<Result<AdminUserDto>> MakeManagerAsync(User user)
    {
        if (!await Roles.RoleExistsAsync(AppRoles.Manager))
            await Roles.CreateAsync(new Role { Name = AppRoles.Manager });
        return await new SetAdminUserRoles.Handler(Users, Db).Handle(new SetAdminUserRoles.Command
        {
            Id = user.Id,
            Roles = new AdminSetUserRolesDto { Roles = [AppRoles.Manager] },
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_only_system_administrator_cannot_be_given_another_role()
    {
        var only = await GivenAsync("only@t.local", AppRoles.SystemAdministrator);

        var result = await MakeManagerAsync(only);

        Assert.False(result.IsSuccess);
        Assert.Equal(SetAdminUserRoles.LastSystemAdministratorMessage, result.Error);
        Assert.True(await Users.IsInRoleAsync(only, AppRoles.SystemAdministrator));
    }

    [Fact]
    public async Task A_deactivated_second_administrator_does_not_count()
    {
        var only = await GivenAsync("only@t.local", AppRoles.SystemAdministrator);
        await GivenAsync("gone@t.local", AppRoles.SystemAdministrator, active: false);

        var result = await MakeManagerAsync(only);

        Assert.False(result.IsSuccess);
        Assert.Equal(SetAdminUserRoles.LastSystemAdministratorMessage, result.Error);
    }

    [Fact]
    public async Task With_another_active_administrator_the_role_can_change()
    {
        var first = await GivenAsync("first@t.local", AppRoles.SystemAdministrator);
        await GivenAsync("second@t.local", AppRoles.SystemAdministrator);

        var result = await MakeManagerAsync(first);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(await Users.IsInRoleAsync(first, AppRoles.Manager));
    }
}
