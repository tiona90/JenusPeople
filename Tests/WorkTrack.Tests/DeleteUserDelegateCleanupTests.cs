using Application.AdminUsers.Commands;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Deleting a user unpicks every reference to them first, but that sweep missed
/// AnnualLeave.DelegateId — so deleting anyone who had been nominated to cover a
/// colleague's leave failed on the foreign key, surfacing as a raw
/// DbUpdateException rather than an answer the admin could act on.
///
/// The in-memory provider does not enforce foreign keys, so these cannot reproduce
/// that exception directly. They pin the observable behaviour instead — the
/// reference is cleared, and the covered request survives — plus the delete
/// behaviour that makes clearing it necessary in the first place.
///
/// These drove AdminUsersController until the delete moved to DeleteAdminUser;
/// they now drive the handler, which is where the sweep lives.
/// </summary>
public class DeleteUserDelegateCleanupTests : IDisposable
{
    private const string AdminUserId = "u-admin";
    private const string EmployeeUserId = "u-employee";
    private const string DelegateUserId = "u-delegate";
    private const string CoveredLeaveId = "L-covered";
    private const string DelegatesOwnLeaveId = "L-delegates-own";

    private readonly ServiceProvider _services;

    public DeleteUserDelegateCleanupTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"delegate-cleanup-{Guid.NewGuid()}")
            // The delete wraps its sweep in a transaction the in-memory provider
            // can only ignore.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        collection.AddIdentityCore<User>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>();

        _services = collection.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();
    private UserManager<User> Users => _services.GetRequiredService<UserManager<User>>();

    private Task<Result<Unit>> DeleteAsync(string userId) =>
        new DeleteAdminUser.Handler(Db, Users).Handle(
            new DeleteAdminUser.Command { Id = userId, RequestingUserId = AdminUserId },
            CancellationToken.None);

    private async Task<User> AddUserAsync(string id, string name)
    {
        var user = new User
        {
            Id = id,
            UserName = $"{name}@test.local",
            Email = $"{name}@test.local",
            DisplayName = name,
            EmailConfirmed = true,
        };

        var created = await Users.CreateAsync(user);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        return user;
    }

    /// <summary>
    /// One employee on leave, with a colleague nominated to cover it. Both have
    /// profiles, since the sweep walks those too.
    /// </summary>
    private async Task SeedCoveredLeaveAsync()
    {
        var db = Db;

        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        await db.SaveChangesAsync();

        await AddUserAsync(AdminUserId, "admin");
        await AddUserAsync(EmployeeUserId, "employee");
        await AddUserAsync(DelegateUserId, "delegate");

        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p-employee", UserId = EmployeeUserId, DepartmentId = 1 });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p-delegate", UserId = DelegateUserId, DepartmentId = 1 });

        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = CoveredLeaveId,
            EmployeeId = EmployeeUserId,
            EmployeeProfileId = "p-employee",
            DelegateId = DelegateUserId,
            DepartmentId = 1,
            Status = AnnualLeaveStatus.Approved,
            Reason = "Family holiday",
            StartDate = new DateTime(2024, 3, 4),
            EndDate = new DateTime(2024, 3, 8),
        });

        await db.SaveChangesAsync();
        // A production delete runs on a fresh request with an empty change tracker.
        // Leaving the seeded leave tracked lets EF fix the reference up in memory,
        // which hides whether the sweep clears it at all.
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Deleting_a_user_who_was_covering_someone_elses_leave_succeeds_and_clears_the_delegate()
    {
        await SeedCoveredLeaveAsync();

        var before = await Db.AnnualLeaves.AsNoTracking().SingleAsync(l => l.Id == CoveredLeaveId);
        Assert.Equal(DelegateUserId, before.DelegateId);

        var result = await DeleteAsync(DelegateUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(await Users.FindByIdAsync(DelegateUserId));

        var covered = await Db.AnnualLeaves.AsNoTracking().SingleAsync(l => l.Id == CoveredLeaveId);
        Assert.Null(covered.DelegateId);
    }

    /// <summary>
    /// Clearing the reference, not deleting the row: the colleague's approved leave
    /// is none of the departing user's business, and losing it would be a worse bug
    /// than the one being fixed.
    /// </summary>
    [Fact]
    public async Task The_covered_leave_survives_the_delete_with_everything_but_the_delegate_intact()
    {
        await SeedCoveredLeaveAsync();

        // A leave of the delegate's own, which should go with them — so this also
        // pins that "clear the reference" did not become "clear everything".
        Db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = DelegatesOwnLeaveId,
            EmployeeId = DelegateUserId,
            EmployeeProfileId = "p-delegate",
            DepartmentId = 1,
            Status = AnnualLeaveStatus.Pending,
            Reason = "Dentist",
            StartDate = new DateTime(2024, 4, 1),
            EndDate = new DateTime(2024, 4, 1),
        });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        Assert.True((await DeleteAsync(DelegateUserId)).IsSuccess);

        var covered = await Db.AnnualLeaves.AsNoTracking().SingleAsync(l => l.Id == CoveredLeaveId);
        Assert.Null(covered.DelegateId);
        Assert.Equal(EmployeeUserId, covered.EmployeeId);
        Assert.Equal(AnnualLeaveStatus.Approved, covered.Status);
        Assert.Equal(new DateTime(2024, 3, 4), covered.StartDate);
        Assert.Equal("Family holiday", covered.Reason);

        Assert.False(await Db.AnnualLeaves.AsNoTracking().AnyAsync(l => l.Id == DelegatesOwnLeaveId));
    }

    /// <summary>
    /// Came with the migration: deleting your own admin account would lock everyone
    /// out of user administration. It was a 400; it is a conflict, which is what
    /// refusing an otherwise well-formed request over current state means.
    /// </summary>
    [Fact]
    public async Task An_admin_cannot_delete_their_own_account()
    {
        await SeedCoveredLeaveAsync();

        var result = await DeleteAsync(AdminUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.NotNull(await Users.FindByIdAsync(AdminUserId));
    }

    [Fact]
    public async Task Deleting_a_user_who_does_not_exist_is_reported_as_missing()
    {
        await SeedCoveredLeaveAsync();

        var result = await DeleteAsync("no-such-user");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
        Assert.Equal("User not found.", result.Error);
    }

    /// <summary>
    /// The reason the null-out is required at all. If this foreign key is ever
    /// relaxed to SetNull the database would handle it, and whoever makes that
    /// change should see this test and decide deliberately what the sweep does
    /// about it.
    /// </summary>
    [Fact]
    public void The_delegate_foreign_key_still_restricts_deletes()
    {
        using var db = TestDb.Create();

        var delegateFk = db.Model
            .FindEntityType(typeof(AnnualLeave))!
            .GetForeignKeys()
            .Single(fk => fk.Properties.Any(p => p.Name == nameof(AnnualLeave.DelegateId)));

        Assert.Equal(DeleteBehavior.Restrict, delegateFk.DeleteBehavior);
    }
}
