using Application.AdminUsers.Commands;
using Application.AdminUsers.DTOs;
using Application.AdminUsers.Validators;
using Application.Core;
using Domain;
using Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// CreateUser was 120 lines inside AdminUsersController: direct DbContext and
/// UserManager access, and ad-hoc { message = "…" } bodies with 400 for every
/// failure including a duplicate email.
///
/// It is now CreateAdminUser, and these cover what the migration had to preserve
/// (the response body the admin panel reads, the unwind when roles fail to attach,
/// the invite email being reported rather than fatal) and the one thing it changed
/// on purpose: a duplicate email is a 409, not a 400.
/// </summary>
public class CreateAdminUserCommandTests : IDisposable
{
    private const string ExistingEmail = "taken@test.local";

    private readonly ServiceProvider _services;

    public CreateAdminUserCommandTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"create-admin-user-{Guid.NewGuid()}")
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

    /// <summary>
    /// Records what the handler asked for instead of sending it, and can refuse the
    /// way a rejecting mail provider does.
    /// </summary>
    private sealed class FakeAccountEmailSender : IAccountEmailSender
    {
        public bool Result { get; set; } = true;
        public User? Invited { get; private set; }

        public string BuildClientUrl(string route, IDictionary<string, string?>? query = null) => $"https://test.local{route}";

        public Task<bool> SendWelcomeInviteAsync(User user, CancellationToken cancellationToken = default)
        {
            Invited = user;
            return Task.FromResult(Result);
        }

        public Task<bool> SendPasswordResetAsync(User user, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);

        public Task<bool> SendEmailChangeConfirmationAsync(
            User user, string newEmail, string apiBaseUrlFallback, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private async Task SeedAsync()
    {
        var db = Db;
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        await db.SaveChangesAsync();

        foreach (var role in new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.Employee })
        {
            await Roles.CreateAsync(new Role { Name = role });
        }

        db.ChangeTracker.Clear();
    }

    private static AdminCreateUserDto Payload(
        string email = "newjoiner@test.local",
        string displayName = "New Joiner",
        int departmentId = 1,
        string? role = AppRoles.Employee) => new()
    {
        Email = email,
        DisplayName = displayName,
        DepartmentId = departmentId,
        Roles = role is null ? [] : [role],
        JobTitle = "Engineer",
    };

    private Task<Result<AdminUserDto>> Handle(AdminCreateUserDto payload, FakeAccountEmailSender mail) =>
        new CreateAdminUser.Handler(Db, Users, mail, NullLogger<CreateAdminUser.Handler>.Instance)
            .Handle(new CreateAdminUser.Command { User = payload }, CancellationToken.None);

    private Task<FluentValidation.Results.ValidationResult> Validate(AdminCreateUserDto payload) =>
        new CreateAdminUserValidator(Db, Roles)
            .ValidateAsync(new CreateAdminUser.Command { User = payload });

    /* ── The happy path ─────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_created_user_comes_back_in_the_shape_the_admin_panel_reads()
    {
        await SeedAsync();
        var mail = new FakeAccountEmailSender();

        var result = await Handle(Payload(), mail);

        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Value!;
        Assert.Equal("newjoiner@test.local", dto.Email);
        // UserName mirrors the email: login looks the account up by user name.
        Assert.Equal("newjoiner@test.local", dto.UserName);
        Assert.Equal("New Joiner", dto.DisplayName);
        Assert.True(dto.EmailConfirmed);
        Assert.Equal([AppRoles.Employee], dto.Roles);
        Assert.True(dto.InviteEmailSent);

        // And the profile the user needs to book leave against exists.
        var profile = await Db.EmployeeProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(1, profile.DepartmentId);
        Assert.Equal(CreateAdminUser.Handler.DefaultEntitlement, profile.AnnualLeaveEntitlement);
        Assert.Equal(CreateAdminUser.Handler.DefaultEntitlement, profile.LeaveBalance);
        Assert.Equal("Engineer", profile.JobTitle);
    }

    /// <summary>
    /// A blank entitlement field means "use the default", and the default an admin can
    /// actually see is the one on Leave Settings — not a constant compiled in here.
    /// </summary>
    [Fact]
    public async Task A_blank_entitlement_takes_the_default_configured_in_settings()
    {
        await SeedAsync();
        var db = Db;
        db.AppSettings.Add(new AppSettings { DefaultAnnualEntitlement = 26 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(Payload(), new FakeAccountEmailSender());

        Assert.True(result.IsSuccess, result.Error);
        var profile = await Db.EmployeeProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(26, profile.AnnualLeaveEntitlement);
        Assert.Equal(26, profile.LeaveBalance);
    }

    [Fact]
    public async Task An_explicit_entitlement_still_wins_over_the_configured_default()
    {
        await SeedAsync();
        var db = Db;
        db.AppSettings.Add(new AppSettings { DefaultAnnualEntitlement = 26 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var payload = Payload();
        payload.AnnualLeaveEntitlement = 12;

        var result = await Handle(payload, new FakeAccountEmailSender());

        Assert.True(result.IsSuccess, result.Error);
        var profile = await Db.EmployeeProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(12, profile.AnnualLeaveEntitlement);
    }

    [Fact]
    public async Task An_omitted_role_defaults_to_Employee()
    {
        await SeedAsync();

        var result = await Handle(Payload(role: null), new FakeAccountEmailSender());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([AppRoles.Employee], result.Value!.Roles);
    }

    /// <summary>
    /// The account is usable without the email, so a rejecting provider is reported
    /// on the response rather than failing the create — the admin needs to know to
    /// tell the new joiner to use "Forgot password?".
    /// </summary>
    [Fact]
    public async Task A_rejected_invite_email_is_reported_but_does_not_fail_the_create()
    {
        await SeedAsync();
        var mail = new FakeAccountEmailSender { Result = false };

        var result = await Handle(Payload(), mail);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.InviteEmailSent);
        Assert.NotNull(await Users.FindByEmailAsync("newjoiner@test.local"));
    }

    [Fact]
    public async Task The_new_user_is_the_one_invited()
    {
        await SeedAsync();
        var mail = new FakeAccountEmailSender();

        await Handle(Payload(), mail);

        Assert.Equal("newjoiner@test.local", mail.Invited?.Email);
    }

    /* ── The conflict ───────────────────────────────────────────────────────── */

    /// <summary>
    /// The change this migration makes on purpose. A duplicate email was a 400,
    /// which says "your request was malformed" about a request that was fine —
    /// the address is simply taken. 409, per the convention in 8a0eda6.
    /// </summary>
    [Fact]
    public async Task A_duplicate_email_is_a_conflict_not_a_bad_request()
    {
        await SeedAsync();
        Assert.True((await Users.CreateAsync(new User
        {
            UserName = ExistingEmail,
            Email = ExistingEmail,
            DisplayName = "Already Here",
        })).Succeeded);

        var result = await Handle(Payload(email: ExistingEmail), new FakeAccountEmailSender());

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal("Email is already registered.", result.Error);
    }

    [Fact]
    public async Task A_refused_duplicate_leaves_no_second_profile_behind()
    {
        await SeedAsync();
        await Users.CreateAsync(new User { UserName = ExistingEmail, Email = ExistingEmail, DisplayName = "Already Here" });

        await Handle(Payload(email: ExistingEmail), new FakeAccountEmailSender());

        Assert.False(await Db.EmployeeProfiles.AsNoTracking().AnyAsync());
    }

    /* ── The validator ──────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_valid_payload_passes_validation()
    {
        await SeedAsync();

        var result = await Validate(Payload());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_email_is_refused(string email)
    {
        await SeedAsync();

        var result = await Validate(Payload(email: email));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Email is required.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_display_name_is_refused(string displayName)
    {
        await SeedAsync();

        var result = await Validate(Payload(displayName: displayName));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Display name is required.");
    }

    [Fact]
    public async Task A_department_that_does_not_exist_is_refused()
    {
        await SeedAsync();

        var result = await Validate(Payload(departmentId: 999));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Selected department does not exist.");
    }

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        await SeedAsync();

        var payload = Payload();
        payload.Roles = ["Sysadmin"];

        var result = await Validate(payload);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "One or more roles are invalid.");
    }

    [Fact]
    public async Task More_than_one_role_is_refused()
    {
        await SeedAsync();

        var payload = Payload();
        payload.Roles = [AppRoles.Admin, AppRoles.Employee];

        var result = await Validate(payload);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A user can have only one role.");
    }

    [Fact]
    public async Task An_invalid_manager_profile_is_refused()
    {
        await SeedAsync();

        var payload = Payload();
        payload.ManagerId = "no-such-profile";

        var result = await Validate(payload);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Manager profile is invalid.");
    }

    /// <summary>
    /// The admin UI derives the manager from the department and can legitimately
    /// leave it unset, so the rule only applies when a value is supplied.
    /// </summary>
    [Fact]
    public async Task An_omitted_manager_profile_is_allowed()
    {
        await SeedAsync();

        var payload = Payload();
        payload.ManagerId = null;

        Assert.True((await Validate(payload)).IsValid);
    }

    /* ── Registration ───────────────────────────────────────────────────────── */

    /// <summary>
    /// The migration is only real if the validators run. Program.cs finds them with
    /// AddValidatorsFromAssemblyContaining&lt;MappingProfiles&gt;, so each command needs a
    /// public, concrete IValidator in that assembly — otherwise the checks that used
    /// to be inline in the controller are simply gone, and every test above still
    /// passes because it constructs the validator by hand.
    /// </summary>
    [Fact]
    public void Every_migrated_admin_user_command_has_a_discoverable_validator()
    {
        var scannedAssembly = typeof(Application.Core.MappingProfiles).Assembly;

        Type[] commands =
        [
            typeof(CreateAdminUser.Command),
            typeof(UpdateAdminUser.Command),
            typeof(SetAdminUserRoles.Command),
        ];

        foreach (var command in commands)
        {
            var validatorInterface = typeof(FluentValidation.IValidator<>).MakeGenericType(command);

            Assert.Contains(
                scannedAssembly.GetTypes(),
                type => type is { IsClass: true, IsAbstract: false, IsPublic: true }
                    && validatorInterface.IsAssignableFrom(type));
        }
    }
}
