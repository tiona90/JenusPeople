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
/// The seeder plants one published constant password into every account it owns
/// and re-asserts it on every startup. Production had Seed:Enabled and
/// Seed:DemoData both true, so each restart of the deployed app reset
/// admin@annualleave.com — and ten demo accounts — back to that password,
/// undoing whatever the operator had set.
///
/// Two things stop that now, and both are tested here: Production configuration no
/// longer switches seeding on, and <see cref="SeedPolicy"/> withholds account
/// creation, password resets and demo accounts on a Production host even when it
/// is switched on. The second is what matters if someone flips the first back.
/// </summary>
public class SeedPolicyTests : IDisposable
{
    private const string AdminEmail = "admin@annualleave.com";
    private const string SeedPassword = "Pa$$w0rd";
    private const string OperatorPassword = "0perator-Chose-Th1s!";
    private static readonly string[] DemoEmails =
    [
        "manager1@annualleave.com",
        "manager2@annualleave.com",
        "employee1a@annualleave.com",
        "employee2d@annualleave.com",
    ];

    private readonly ServiceProvider _services;

    public SeedPolicyTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"seed-policy-{Guid.NewGuid()}")
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

    private static SeedPolicy ProductionPolicy(bool demoData = true, bool allowInProduction = false) =>
        SeedPolicy.For("Production", demoData, allowInProduction);

    private async Task<User> GivenExistingAdminAsync(string password)
    {
        var admin = new User
        {
            DisplayName = "Renamed By Operator",
            UserName = AdminEmail,
            Email = AdminEmail,
            EmailConfirmed = true,
        };

        var created = await Users.CreateAsync(admin, password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        return admin;
    }

    // ── The rule itself ─────────────────────────────────────────────────────────

    [Fact]
    public void Production_without_the_override_withholds_passwords_and_demo_data()
    {
        var policy = SeedPolicy.For("Production", demoData: true, allowInProduction: false);

        Assert.True(policy.RestrictedForProduction);
        Assert.False(policy.ManageSeedPasswords);
        Assert.False(policy.SeedDemoData);
    }

    [Fact]
    public void Production_with_the_override_permits_everything_it_was_asked_for()
    {
        var policy = SeedPolicy.For("Production", demoData: true, allowInProduction: true);

        Assert.False(policy.RestrictedForProduction);
        Assert.True(policy.ManageSeedPasswords);
        Assert.True(policy.SeedDemoData);
    }

    /// <summary>
    /// The override lifts the Production restriction; it does not switch demo data
    /// on for someone who never asked for it.
    /// </summary>
    [Fact]
    public void The_override_does_not_by_itself_enable_demo_data()
    {
        var policy = SeedPolicy.For("Production", demoData: false, allowInProduction: true);

        Assert.True(policy.ManageSeedPasswords);
        Assert.False(policy.SeedDemoData);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData(null)]
    public void Any_other_environment_seeds_as_asked(string? environmentName)
    {
        var policy = SeedPolicy.For(environmentName, demoData: true, allowInProduction: false);

        Assert.False(policy.RestrictedForProduction);
        Assert.True(policy.ManageSeedPasswords);
        Assert.True(policy.SeedDemoData);
    }

    /// <summary>
    /// ASPNETCORE_ENVIRONMENT is a free-text string that people do write in other
    /// casings, and a case-sensitive comparison here would silently unlock every
    /// restriction on a live host.
    /// </summary>
    [Theory]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    [InlineData("Production")]
    public void The_environment_name_is_matched_case_insensitively(string environmentName)
    {
        Assert.True(SeedPolicy.For(environmentName, demoData: true, allowInProduction: false)
            .RestrictedForProduction);
    }

    // ── What the rule does to the seeder ────────────────────────────────────────

    /// <summary>
    /// The regression this whole change exists for: an operator changes the admin
    /// password, the app restarts, and the seeder must not put it back.
    /// </summary>
    [Fact]
    public async Task Production_leaves_an_existing_admin_password_alone()
    {
        var admin = await GivenExistingAdminAsync(OperatorPassword);

        await SeedAsync(ProductionPolicy());

        Assert.True(await Users.CheckPasswordAsync(admin, OperatorPassword));
        Assert.False(await Users.CheckPasswordAsync(admin, SeedPassword));
    }

    /// <summary>
    /// The counterpart, and the guard against a test that would pass on a seeder
    /// that had simply stopped working: with the restriction lifted, the reset the
    /// test above forbids does happen.
    /// </summary>
    [Fact]
    public async Task An_unrestricted_policy_still_resets_the_seed_password()
    {
        var admin = await GivenExistingAdminAsync(OperatorPassword);

        await SeedAsync(SeedPolicy.Unrestricted());

        Assert.True(await Users.CheckPasswordAsync(admin, SeedPassword));
        Assert.False(await Users.CheckPasswordAsync(admin, OperatorPassword));
    }

    /// <summary>
    /// The non-password reconciliation is safe to repeat, so it is not withheld —
    /// only the credential handling is.
    /// </summary>
    [Fact]
    public async Task Production_still_reconciles_the_admin_role_and_display_name()
    {
        var admin = await GivenExistingAdminAsync(OperatorPassword);

        await SeedAsync(ProductionPolicy());

        var reloaded = await Users.FindByEmailAsync(AdminEmail);
        Assert.NotNull(reloaded);
        Assert.Equal("Admin User", reloaded!.DisplayName);
        Assert.True(await Users.IsInRoleAsync(reloaded, AppRoles.Admin));
        Assert.True(await Users.CheckPasswordAsync(admin, OperatorPassword));
    }

    /// <summary>
    /// Creating a seed account means giving it the published password, so on
    /// Production the account is left absent instead. An empty deployment is a
    /// visible problem; an admin account with a known password is not.
    /// </summary>
    [Fact]
    public async Task Production_does_not_create_the_admin_account()
    {
        await SeedAsync(ProductionPolicy());

        Assert.Null(await Users.FindByEmailAsync(AdminEmail));
    }

    [Fact]
    public async Task An_unrestricted_policy_does_create_the_admin_account()
    {
        await SeedAsync(SeedPolicy.Unrestricted());

        var admin = await Users.FindByEmailAsync(AdminEmail);
        Assert.NotNull(admin);
        Assert.True(await Users.CheckPasswordAsync(admin!, SeedPassword));
    }

    [Fact]
    public async Task Production_does_not_seed_demo_accounts_even_when_demo_data_is_configured()
    {
        await SeedAsync(ProductionPolicy(demoData: true));

        foreach (var email in DemoEmails)
        {
            Assert.Null(await Users.FindByEmailAsync(email));
        }
    }

    [Fact]
    public async Task An_unrestricted_policy_does_seed_demo_accounts()
    {
        await SeedAsync(SeedPolicy.Unrestricted(demoData: true));

        foreach (var email in DemoEmails)
        {
            Assert.NotNull(await Users.FindByEmailAsync(email));
        }
    }

    /// <summary>
    /// Removing demo accounts a previous deploy left behind only ever reduces the
    /// attack surface, so the Production restriction must not disable it — the
    /// deployment that created these ten accounts with a published password is
    /// exactly the one that needs them cleaned up.
    /// </summary>
    [Fact]
    public async Task Production_still_deletes_demo_accounts_a_previous_deploy_created()
    {
        foreach (var email in DemoEmails)
        {
            var created = await Users.CreateAsync(
                new User { DisplayName = email, UserName = email, Email = email, EmailConfirmed = true },
                SeedPassword);
            Assert.True(created.Succeeded);
        }

        await SeedAsync(ProductionPolicy());

        foreach (var email in DemoEmails)
        {
            Assert.Null(await Users.FindByEmailAsync(email));
        }
    }

    /// <summary>
    /// Reference data carries no credentials, so a Production seed run is still
    /// worth something — the restriction is targeted, not a blanket refusal.
    /// </summary>
    [Fact]
    public async Task Production_still_seeds_reference_data()
    {
        await SeedAsync(ProductionPolicy());

        Assert.NotEmpty(await Db.Roles.ToListAsync());
        Assert.NotEmpty(await Db.Departments.ToListAsync());
        Assert.NotEmpty(await Db.LeaveTypes.ToListAsync());
        Assert.NotEmpty(await Db.ProjectActivityTypes.ToListAsync());
        Assert.NotEmpty(await Db.AppSettings.ToListAsync());
    }

    // ── Reference data and illustrative content are separate ────────────────────

    /// <summary>
    /// The four demo seeders wrote content regardless of which users existed, so
    /// withholding the demo *accounts* did not withhold the sample projects, leave
    /// request, timesheet or entries — on a live database they read as staff having
    /// booked leave and logged hours nobody logged. They are gated on SeedDemoData
    /// now, not left to degrade.
    ///
    /// The admin already exists here: that is the case that used to leak, since a
    /// missing admin was the only thing stopping the leave and timesheet rows.
    /// </summary>
    [Fact]
    public async Task No_demo_business_data_is_written_without_demo_data()
    {
        await GivenExistingAdminAsync(OperatorPassword);

        await SeedAsync(SeedPolicy.Unrestricted(demoData: false));

        Assert.Empty(await Db.Projects.ToListAsync());
        Assert.Empty(await Db.AnnualLeaves.ToListAsync());
        Assert.Empty(await Db.Timesheets.ToListAsync());
        Assert.Empty(await Db.TimesheetEntries.ToListAsync());
    }

    [Fact]
    public async Task Production_writes_no_demo_business_data()
    {
        await GivenExistingAdminAsync(OperatorPassword);

        await SeedAsync(ProductionPolicy(demoData: true));

        Assert.Empty(await Db.Projects.ToListAsync());
        Assert.Empty(await Db.AnnualLeaves.ToListAsync());
        Assert.Empty(await Db.Timesheets.ToListAsync());
        Assert.Empty(await Db.TimesheetEntries.ToListAsync());
    }

    /// <summary>
    /// The other half of the split: a demo host still gets the worked examples, so
    /// the gate is the thing that changed and not the seeders themselves.
    /// </summary>
    [Fact]
    public async Task Demo_data_still_writes_the_sample_projects_and_business_data()
    {
        await SeedAsync(SeedPolicy.Unrestricted(demoData: true));

        Assert.Equal(3, (await Db.Projects.ToListAsync()).Count);
        Assert.NotEmpty(await Db.AnnualLeaves.ToListAsync());
        Assert.NotEmpty(await Db.Timesheets.ToListAsync());
        Assert.NotEmpty(await Db.TimesheetEntries.ToListAsync());
    }

    /// <summary>
    /// The admin's own profile and department assignment are structural — leave and
    /// timesheets need a profile to hang off — so they are not demo content and
    /// survive on a Production host. They self-limit to the admin because the demo
    /// users they would otherwise cover no longer exist.
    /// </summary>
    [Fact]
    public async Task Production_still_gives_an_existing_admin_a_profile_and_department()
    {
        var admin = await GivenExistingAdminAsync(OperatorPassword);

        await SeedAsync(ProductionPolicy());

        var profiles = await Db.EmployeeProfiles.ToListAsync();
        var assignments = await Db.UserDepartments.ToListAsync();

        Assert.Single(profiles);
        Assert.Equal(admin.Id, profiles[0].UserId);
        Assert.Single(assignments);
        Assert.Equal(admin.Id, assignments[0].UserId);
    }

    /// <summary>
    /// BackfillProjectMetadata used to be reachable only through SeedProjects' early
    /// return, so gating SeedProjects on demo data would have stopped repairing real
    /// project rows. SeedData calls it directly now — a project row predating the
    /// metadata migration gets fixed on a host that seeds no demo data at all.
    /// </summary>
    [Fact]
    public async Task Real_project_rows_are_repaired_even_when_demo_data_is_off()
    {
        var admin = await GivenExistingAdminAsync(OperatorPassword);
        Db.Departments.Add(new Department { Name = "Engineering", Code = "ENG", IsActive = true });
        await Db.SaveChangesAsync();

        Db.Projects.Add(new Project
        {
            Name = "A Real Project",
            Code = "REAL-001",
            DepartmentId = Db.Departments.First().Id,
            Status = ProjectStatus.Active,
            IsActive = true,
            ColorKey = string.Empty,
            TargetWeeklyHours = 0,
            TargetMonthlyHours = 0,
        });
        await Db.SaveChangesAsync();

        await SeedAsync(ProductionPolicy());

        var project = await Db.Projects.SingleAsync();
        Assert.False(string.IsNullOrEmpty(project.ColorKey));
        Assert.NotEqual(0, project.TargetWeeklyHours);
        Assert.NotEqual(0, project.TargetMonthlyHours);
        Assert.Equal(admin.Id, project.OwnerId);
    }
}
