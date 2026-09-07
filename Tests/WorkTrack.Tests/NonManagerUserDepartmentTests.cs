using Application.AdminUsers.Commands;
using Application.AdminUsers.DTOs;
using Application.Core;
using Application.Departments.Commands;
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
/// A <see cref="UserDepartment"/> row means one thing: an extra department a
/// <b>manager</b> covers, beyond the one on their own profile. Nothing reads it
/// for anyone else — <c>ProjectScope.DepartmentIdsForAsync</c> consults the table
/// only when the caller is a manager, and an Admin short-circuits to "sees
/// everything" before departments are resolved at all.
///
/// One place did read it for everyone: <c>DeleteDepartment</c>, which counts every
/// row as an "assigned manager" blocker. So a row that granted nothing still made
/// its department undeletable — and unblockable, because the only
/// <c>UserDepartments</c> route is a GET, no client code calls even that, and the
/// sole delete path is a side effect of deleting the user outright.
///
/// Two paths wrote such rows:
///
/// <list type="bullet">
/// <item>The seeder gave <c>admin@annualleave.com</c> ENG unconditionally — in
/// every environment, before the demo-data gate — so the deployed IIS site had a
/// permanently undeletable Engineering department. It seeded an Employee into HR
/// as well.</item>
/// <item><c>SetAdminUserRoles</c> demotes a manager without touching the table, and
/// a user holds exactly one role, so every demotion out of Manager left its rows
/// behind.</item>
/// </list>
/// </summary>
public class NonManagerUserDepartmentTests : IDisposable
{
    private const string AdminEmail = "admin@annualleave.com";

    /// <summary>
    /// Deliberately not one of the seeder's own demo managers. Those are deleted
    /// outright by a Production seed run, which takes their assignments with them —
    /// a test using one would pass whether or not the cleanup could tell a manager
    /// apart from anyone else.
    /// </summary>
    private const string ManagerEmail = "real.manager@worktrack.local";

    private readonly ServiceProvider _services;

    public NonManagerUserDepartmentTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"non-manager-ud-{Guid.NewGuid()}")
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

    private Task SeedAsync(SeedPolicy policy) => DbInitializer.SeedData(Db, Users, Roles, policy);

    private async Task<User> GivenUserAsync(string email, string role)
    {
        var user = new User
        {
            DisplayName = email,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
        };

        var created = await Users.CreateAsync(user, "Pa$$w0rd");
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));

        if (!await Roles.RoleExistsAsync(role))
        {
            Assert.True((await Roles.CreateAsync(new Role { Name = role })).Succeeded);
        }

        Assert.True((await Users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private async Task<int> GivenDepartmentAsync(string name, string code)
    {
        var department = new Department { Name = name, Code = code, IsActive = true };
        Db.Departments.Add(department);
        await Db.SaveChangesAsync();
        return department.Id;
    }

    private async Task GivenAssignmentAsync(string userId, int departmentId)
    {
        Db.UserDepartments.Add(new UserDepartment
        {
            UserId = userId,
            DepartmentId = departmentId,
            AssignedAt = DateTime.UtcNow,
        });
        await Db.SaveChangesAsync();
    }

    // ── The seeder no longer writes the rows ────────────────────────────────────

    /// <summary>
    /// The IIS case. A real deployment has no demo users, so after this change the
    /// seeder writes no assignments whatsoever — where it previously wrote the one
    /// that jammed Engineering.
    /// </summary>
    [Fact]
    public async Task A_production_seed_writes_no_department_assignments()
    {
        await SeedAsync(SeedPolicy.For("Production", demoData: true, allowInProduction: false));

        Assert.Empty(await Db.UserDepartments.ToListAsync());
    }

    /// <summary>
    /// A demo host still gets one, so the seeder has not simply been switched off —
    /// and every row it writes belongs to someone in the Manager role.
    /// </summary>
    [Fact]
    public async Task A_demo_seed_writes_assignments_only_for_managers()
    {
        await SeedAsync(SeedPolicy.Unrestricted(demoData: true));

        var assignments = await Db.UserDepartments.ToListAsync();
        Assert.NotEmpty(assignments);

        foreach (var assignment in assignments)
        {
            var user = await Users.FindByIdAsync(assignment.UserId);
            Assert.NotNull(user);
            Assert.True(
                await Users.IsInRoleAsync(user!, AppRoles.Manager),
                $"{user!.Email} holds a department assignment without the Manager role.");
        }
    }

    // ── And clears the ones already written ─────────────────────────────────────

    /// <summary>
    /// What actually unjams the deployed database: the row is already there, and no
    /// admin can reach it, so startup has to clear it.
    /// </summary>
    [Fact]
    public async Task Seeding_deletes_an_existing_admins_department_assignment()
    {
        var admin = await GivenUserAsync(AdminEmail, AppRoles.Admin);
        var engineering = await GivenDepartmentAsync("Engineering", "ENG");
        await GivenAssignmentAsync(admin.Id, engineering);

        Assert.Single(await Db.UserDepartments.ToListAsync());

        await SeedAsync(SeedPolicy.For("Production", demoData: false, allowInProduction: false));

        Assert.Empty(await Db.UserDepartments.ToListAsync());
    }

    /// <summary>An Employee's row is equally inert, and equally blocking.</summary>
    [Fact]
    public async Task Seeding_deletes_an_existing_employees_department_assignment()
    {
        var employee = await GivenUserAsync("employee1a@annualleave.com", AppRoles.Employee);
        var hr = await GivenDepartmentAsync("Human Resources", "HR");
        await GivenAssignmentAsync(employee.Id, hr);

        await SeedAsync(SeedPolicy.For("Production", demoData: false, allowInProduction: false));

        Assert.Empty(await Db.UserDepartments.ToListAsync());
    }

    /// <summary>
    /// The guard against a cleanup that fixed the symptom by emptying the table: a
    /// manager's assignment is the real feature and has to survive every startup.
    /// </summary>
    [Fact]
    public async Task Seeding_keeps_a_managers_department_assignment()
    {
        var manager = await GivenUserAsync(ManagerEmail, AppRoles.Manager);
        var hr = await GivenDepartmentAsync("Human Resources", "HR");
        await GivenAssignmentAsync(manager.Id, hr);

        await SeedAsync(SeedPolicy.For("Production", demoData: false, allowInProduction: false));

        var assignment = Assert.Single(await Db.UserDepartments.ToListAsync());
        Assert.Equal(manager.Id, assignment.UserId);
        Assert.Equal(hr, assignment.DepartmentId);
    }

    // ── Demotion does not put them back ─────────────────────────────────────────

    private Task<Result<AdminUserDto>> SetRole(string userId, string role) =>
        new SetAdminUserRoles.Handler(Users, Db).Handle(
            new SetAdminUserRoles.Command
            {
                Id = userId,
                Roles = new AdminSetUserRolesDto { Roles = [role] },
            },
            CancellationToken.None);

    /// <summary>
    /// A user holds exactly one role, so moving a manager to Employee always strips
    /// Manager — and used to strand their assignments, recreating this bug between
    /// restarts on a database the startup cleanup had already fixed.
    /// </summary>
    [Fact]
    public async Task Demoting_a_manager_clears_their_department_assignments()
    {
        var manager = await GivenUserAsync(ManagerEmail, AppRoles.Manager);
        Assert.True((await Roles.CreateAsync(new Role { Name = AppRoles.Employee })).Succeeded);
        var hr = await GivenDepartmentAsync("Human Resources", "HR");
        await GivenAssignmentAsync(manager.Id, hr);

        var result = await SetRole(manager.Id, AppRoles.Employee);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(await Db.UserDepartments.ToListAsync());
    }

    /// <summary>
    /// Re-asserting the role a manager already has must not throw their assignments
    /// away — the cleanup keys off the role they end up with, not off the fact that
    /// the command ran.
    /// </summary>
    [Fact]
    public async Task Re_asserting_the_manager_role_keeps_their_department_assignments()
    {
        var manager = await GivenUserAsync(ManagerEmail, AppRoles.Manager);
        var hr = await GivenDepartmentAsync("Human Resources", "HR");
        await GivenAssignmentAsync(manager.Id, hr);

        var result = await SetRole(manager.Id, AppRoles.Manager);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(await Db.UserDepartments.ToListAsync());
    }

    /// <summary>
    /// Why the rows are deleted rather than merely excluded from
    /// <c>DeleteDepartment</c>'s count. The foreign key is <c>Restrict</c>, so a row
    /// the pre-check chose to ignore would still refuse the delete on SaveChanges —
    /// trading the explained 409 for the catch-all one. Needs a provider that
    /// enforces foreign keys, which the in-memory one above does not.
    /// </summary>
    [Fact]
    public async Task An_ignored_assignment_would_still_block_the_delete_at_the_database()
    {
        await using var db = await TransactionalTestDb.CreateAsync();

        db.Departments.Add(new Department { Id = 1, Name = "Human Resources", Code = "HR" });
        db.Users.Add(new User
        {
            Id = "u-admin",
            UserName = AdminEmail,
            Email = AdminEmail,
            DisplayName = "Admin User",
        });
        await db.SaveChangesAsync();

        db.UserDepartments.Add(new UserDepartment
        {
            UserId = "u-admin",
            DepartmentId = 1,
            AssignedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var blocked = await new DeleteDepartment.Handler(db).Handle(
            new DeleteDepartment.Command { Id = 1 }, CancellationToken.None);

        Assert.False(blocked.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, blocked.ErrorKind);

        // With the row gone — as the cleanup leaves it — the same delete goes through.
        db.ChangeTracker.Clear();
        db.UserDepartments.RemoveRange(await db.UserDepartments.ToListAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var allowed = await new DeleteDepartment.Handler(db).Handle(
            new DeleteDepartment.Command { Id = 1 }, CancellationToken.None);

        Assert.True(allowed.IsSuccess, allowed.Error);
        Assert.Empty(await db.Departments.ToListAsync());
    }
}
