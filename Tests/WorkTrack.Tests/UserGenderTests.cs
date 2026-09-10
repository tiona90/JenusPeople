using Application.AdminUsers.Commands;
using Application.AdminUsers.DTOs;
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
/// Gender is recorded HR data an administrator maintains, and nothing consults it —
/// so what there is to cover is the plumbing: that it survives a create, survives
/// an edit, and can be taken back off again.
///
/// The clearing case is the one worth having. <see cref="AdminUpdateUserDto"/> is a
/// full-replace DTO, so a null in the request must genuinely null the column. Had
/// gender followed the "null leaves the stored answer alone" convention used by
/// <c>HasChildrenDeclaration</c>, the dialog's "Not specified" option would have
/// looked like it worked and silently done nothing.
/// </summary>
public class UserGenderTests : IDisposable
{
    private readonly ServiceProvider _services;

    public UserGenderTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"user-gender-{Guid.NewGuid()}")
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

    private sealed class SilentAccountEmailSender : IAccountEmailSender
    {
        public string BuildClientUrl(string route, IDictionary<string, string?>? query = null) => $"https://test.local{route}";

        public Task<bool> SendWelcomeInviteAsync(User user, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendPasswordResetAsync(User user, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendEmailChangeConfirmationAsync(
            User user, string newEmail, string apiBaseUrlFallback, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
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

    private Task<Result<AdminUserDto>> Create(Gender? gender) =>
        new CreateAdminUser.Handler(Db, Users, new SilentAccountEmailSender(), NullLogger<CreateAdminUser.Handler>.Instance)
            .Handle(new CreateAdminUser.Command
            {
                User = new AdminCreateUserDto
                {
                    Email = "newjoiner@test.local",
                    DisplayName = "New Joiner",
                    DepartmentId = 1,
                    Roles = [AppRoles.Employee],
                    Gender = gender,
                },
            }, CancellationToken.None);

    private Task<Result<AdminUserDto>> Update(string id, Gender? gender) =>
        new UpdateAdminUser.Handler(Users).Handle(new UpdateAdminUser.Command
        {
            Id = id,
            User = new AdminUpdateUserDto
            {
                Email = "newjoiner@test.local",
                DisplayName = "New Joiner",
                Gender = gender,
            },
        }, CancellationToken.None);

    private async Task<Gender?> StoredGenderAsync(string id) =>
        (await Db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).Gender;

    [Theory]
    [InlineData(Gender.Male)]
    [InlineData(Gender.Female)]
    public async Task A_gender_chosen_on_create_is_stored_and_returned(Gender gender)
    {
        await SeedAsync();

        var result = await Create(gender);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(gender, result.Value!.Gender);
        Assert.Equal(gender, await StoredGenderAsync(result.Value!.Id));
    }

    /// <summary>
    /// Every account predating the column has no value, and the create dialog's
    /// "Not specified" is the same thing — so an omitted gender must stay unset
    /// rather than defaulting to either answer on the person's behalf.
    /// </summary>
    [Fact]
    public async Task An_omitted_gender_stays_unset()
    {
        await SeedAsync();

        var result = await Create(null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.Gender);
        Assert.Null(await StoredGenderAsync(result.Value!.Id));
    }

    [Fact]
    public async Task An_edit_can_set_a_gender_that_was_never_specified()
    {
        await SeedAsync();
        var id = (await Create(null)).Value!.Id;

        var result = await Update(id, Gender.Female);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Gender.Female, result.Value!.Gender);
        Assert.Equal(Gender.Female, await StoredGenderAsync(id));
    }

    [Fact]
    public async Task An_edit_can_change_a_stored_gender()
    {
        await SeedAsync();
        var id = (await Create(Gender.Male)).Value!.Id;

        var result = await Update(id, Gender.Female);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Gender.Female, await StoredGenderAsync(id));
    }

    /// <summary>
    /// The one that matters: "Not specified" has to be able to undo a value set by
    /// mistake. A patch-style handler would have left the old answer in place.
    /// </summary>
    [Fact]
    public async Task An_edit_with_no_gender_clears_the_stored_one()
    {
        await SeedAsync();
        var id = (await Create(Gender.Male)).Value!.Id;
        Assert.Equal(Gender.Male, await StoredGenderAsync(id));

        var result = await Update(id, null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.Gender);
        Assert.Null(await StoredGenderAsync(id));
    }
}
