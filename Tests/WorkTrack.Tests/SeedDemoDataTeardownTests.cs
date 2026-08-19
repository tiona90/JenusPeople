using Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Turning Seed:DemoData off is the documented way to get the ten demo accounts —
/// and the password published in this repository — off a database that has already
/// been seeded with them. It did not work.
///
/// Every FK into a user row is DeleteBehavior.Restrict, so the database refuses the
/// DELETE rather than tidying up after it. DbInitializer.CleanupUserDependencies had
/// drifted behind the equivalent code in DeleteAdminUser and no longer detached
/// Project.OwnerId — which the seeded demo projects set to manager1/manager2. So
/// deleting manager1 threw, Program.cs logged it and carried on, and all ten demo
/// accounts survived the restart that was supposed to remove them.
///
/// These run on SQLite rather than the EF in-memory provider on purpose: the
/// in-memory provider enforces no foreign keys, so this class of bug is invisible to
/// it and every assertion below passes whether or not the code does anything.
/// </summary>
public class SeedDemoDataTeardownTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@annualleave.com";
    private static readonly string[] DemoEmails =
    [
        "manager1@annualleave.com",
        "manager2@annualleave.com",
        "employee1a@annualleave.com",
        "employee1b@annualleave.com",
        "employee1c@annualleave.com",
        "employee1d@annualleave.com",
        "employee2a@annualleave.com",
        "employee2b@annualleave.com",
        "employee2c@annualleave.com",
        "employee2d@annualleave.com",
    ];

    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var collection = new ServiceCollection();
        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        collection.AddIdentityCore<User>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>();

        _services = collection.BuildServiceProvider();
        await Db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();
    private UserManager<User> Users => _services.GetRequiredService<UserManager<User>>();
    private RoleManager<Role> Roles => _services.GetRequiredService<RoleManager<Role>>();

    /// <summary>
    /// One startup. The change tracker is cleared afterwards because a restart gets a
    /// fresh scope and therefore an empty tracker — and that detail decides the
    /// outcome: with the demo projects still tracked from the seeding run, EF fixes
    /// up the severed FK client-side and the delete succeeds, hiding the bug.
    /// </summary>
    private async Task StartupAsync(SeedPolicy policy)
    {
        await DbInitializer.SeedData(Db, Users, Roles, policy);
        Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Turning_demo_data_off_removes_the_demo_accounts()
    {
        await StartupAsync(SeedPolicy.Unrestricted(demoData: true));
        Assert.Equal(11, await Db.Users.CountAsync());

        await StartupAsync(SeedPolicy.Unrestricted(demoData: false));

        foreach (var email in DemoEmails)
        {
            Assert.Null(await Users.FindByEmailAsync(email));
        }

        Assert.NotNull(await Users.FindByEmailAsync(AdminEmail));
        Assert.Equal(1, await Db.Users.CountAsync());
    }

    /// <summary>
    /// The same transition on a Production host, which is the one that matters: the
    /// accounts carrying the published password have to go even though the policy is
    /// withholding everything else.
    /// </summary>
    [Fact]
    public async Task A_production_startup_removes_demo_accounts_a_previous_deploy_seeded()
    {
        await StartupAsync(SeedPolicy.Unrestricted(demoData: true));

        await StartupAsync(SeedPolicy.For("Production", demoData: true, allowInProduction: false));

        foreach (var email in DemoEmails)
        {
            Assert.Null(await Users.FindByEmailAsync(email));
        }
    }

    /// <summary>
    /// Deleting the owner must detach the project, not delete it. A project is real
    /// work — losing it because the account that happened to own it was removed would
    /// be a worse bug than the one this fixes.
    /// </summary>
    [Fact]
    public async Task Projects_owned_by_a_removed_demo_account_survive_it_unowned()
    {
        await StartupAsync(SeedPolicy.Unrestricted(demoData: true));

        var manager1 = await Users.FindByEmailAsync("manager1@annualleave.com");
        Assert.NotNull(manager1);
        Assert.Contains(await Db.Projects.ToListAsync(), p => p.OwnerId == manager1!.Id);

        await StartupAsync(SeedPolicy.Unrestricted(demoData: false));

        var projects = await Db.Projects.ToListAsync();
        Assert.Equal(3, projects.Count);
        Assert.DoesNotContain(projects, p => p.OwnerId == manager1!.Id);
    }

    /// <summary>
    /// The failed delete used to abort the rest of the run, because SeedUsers is the
    /// second seeder and the exception propagated out of SeedData. Reference data
    /// landing proves the run completed rather than dying at the first seeder.
    /// </summary>
    [Fact]
    public async Task The_startup_that_removes_them_completes_the_rest_of_the_run()
    {
        await StartupAsync(SeedPolicy.Unrestricted(demoData: true));
        await StartupAsync(SeedPolicy.Unrestricted(demoData: false));

        Assert.NotEmpty(await Db.Roles.ToListAsync());
        Assert.NotEmpty(await Db.LeaveTypes.ToListAsync());
        Assert.NotEmpty(await Db.ProjectActivityTypes.ToListAsync());
        Assert.NotEmpty(await Db.AppSettings.ToListAsync());
    }

    /// <summary>
    /// A demo account that logged hours during a UAT session hits two more Restrict
    /// FKs the seeder's cleanup was missing: Timesheet.ApproverId on anything it
    /// approved, and Timesheet.EmployeeId on its own, which points at the profile the
    /// cleanup deletes.
    /// </summary>
    [Fact]
    public async Task A_demo_account_with_timesheets_can_still_be_removed()
    {
        await StartupAsync(SeedPolicy.Unrestricted(demoData: true));

        var manager1 = await Users.FindByEmailAsync("manager1@annualleave.com");
        var employee = await Users.FindByEmailAsync("employee1a@annualleave.com");
        Assert.NotNull(manager1);
        Assert.NotNull(employee);

        var employeeProfile = await Db.EmployeeProfiles.FirstAsync(ep => ep.UserId == employee!.Id);
        var department = await Db.Departments.FirstAsync();

        // The employee's own timesheet, approved by the manager — so removing either
        // account has to detach or delete this row first.
        Db.Timesheets.Add(new Timesheet
        {
            EmployeeId = employeeProfile.Id,
            DepartmentId = department.Id,
            PeriodStart = DateTime.UtcNow.Date.AddDays(-7),
            PeriodEnd = DateTime.UtcNow.Date,
            TotalHours = 40,
            Status = TimesheetStatus.Approved,
            ApproverId = manager1!.Id,
            ApprovedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        await StartupAsync(SeedPolicy.Unrestricted(demoData: false));

        Assert.Null(await Users.FindByEmailAsync("employee1a@annualleave.com"));
        Assert.Null(await Users.FindByEmailAsync("manager1@annualleave.com"));
    }
}
