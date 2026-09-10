using Application.Children.Commands;
using Application.Children.DTOs;
using Application.Core;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A child's row carries the ledger that proves how much per-child leave was taken
/// for them, so it cannot be deleted out from under approved leave. A child who has
/// aged out is kept, not deleted — their history still matters, and they simply read
/// as ineligible.
///
/// TransactionalTestDb, not TestDb: the EF in-memory provider enforces no foreign
/// keys at all, so an assertion about one passes whether or not the model says
/// anything.
/// </summary>
public class ChildCrudTests
{
    private static async Task<EmployeeProfile> SeedProfileAsync(AppDbContext db, string userId = "user-1")
    {
        var user = new User { Id = userId, UserName = $"{userId}@example.com", Email = $"{userId}@example.com", DisplayName = "Andreas Georgiou" };
        var profile = new EmployeeProfile { Id = $"profile-{userId}", UserId = userId };
        db.Users.Add(user);
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    [Fact]
    public async Task Leave_holds_its_child_row_in_place()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        var profile = await SeedProfileAsync(db);

        var child = new Child { EmployeeProfileId = profile.Id, Name = "Andreas", DateOfBirth = new DateOnly(2019, 3, 4) };
        db.Children.Add(child);
        await db.SaveChangesAsync();

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = profile.UserId,
            EmployeeProfileId = profile.Id,
            ChildId = child.Id,
            StartDate = new DateTime(2026, 3, 2),
            EndDate = new DateTime(2026, 3, 6),
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        // A production delete runs on a fresh request with an empty change tracker.
        // Reusing the tracked `child` reference here would let EF's own navigation
        // fixup (child.LeaveRequests was wired up to the leave above by tracking,
        // not by anything this test did) null the foreign key on the leave before
        // the delete reaches the database — hiding whether the restrict constraint
        // holds at all. Clearing the tracker and removing a bare stub, like the
        // sweep in DeleteAdminUser is tested, forces the delete straight to SQLite.
        var childId = child.Id;
        db.ChangeTracker.Clear();
        db.Children.Remove(new Child { Id = childId });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_profile_takes_its_children_with_it()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        var profile = await SeedProfileAsync(db);

        db.Children.Add(new Child { EmployeeProfileId = profile.Id, Name = "Maria", DateOfBirth = new DateOnly(2022, 9, 12) });
        await db.SaveChangesAsync();

        db.EmployeeProfiles.Remove(profile);
        await db.SaveChangesAsync();

        Assert.Empty(await db.Children.ToListAsync());
    }

    private static Task<Result<ChildDto>> Create(AppDbContext db, string callerUserId, UpsertChildRequest child, string? employeeId = null, bool isAdmin = false) =>
        new CreateChild.Handler(db).Handle(new CreateChild.Command
        {
            EmployeeId = employeeId,
            Child = child,
            CallerUserId = callerUserId,
            IsAdmin = isAdmin,
        }, CancellationToken.None);

    private static UpsertChildRequest Andreas() => new() { Name = "Andreas", DateOfBirth = new DateOnly(2019, 3, 4) };

    [Fact]
    public async Task Adding_a_child_declares_that_the_employee_has_children()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        Assert.Null(profile.HasChildren);

        var result = await Create(db, profile.UserId, Andreas());

        Assert.True(result.IsSuccess);
        Assert.Equal("Andreas", result.Value!.Name);
        // Computed, never stored: the age moves on its own as the date passes.
        Assert.Equal(new DateOnly(2034, 3, 3), result.Value.LastEligibleDate);
        Assert.True(result.Value.IsEligible);

        var reloaded = await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id);
        Assert.True(reloaded.HasChildren);
    }

    /// <summary>
    /// The child list is personal data. One employee may not read or write another's,
    /// whatever employee id they put on the request.
    /// </summary>
    [Fact]
    public async Task An_employee_cannot_add_a_child_to_someone_else()
    {
        await using var db = TestDb.Create();
        await SeedProfileAsync(db, "user-1");
        await SeedProfileAsync(db, "user-2");

        var result = await Create(db, "user-1", Andreas(), employeeId: "user-2");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }

    [Fact]
    public async Task An_admin_can_add_a_child_for_anyone()
    {
        await using var db = TestDb.Create();
        await SeedProfileAsync(db, "admin-1");
        var employee = await SeedProfileAsync(db, "user-2");

        var result = await Create(db, "admin-1", Andreas(), employeeId: "user-2", isAdmin: true);

        Assert.True(result.IsSuccess);
        var stored = await db.Children.SingleAsync();
        Assert.Equal(employee.Id, stored.EmployeeProfileId);
    }

    [Fact]
    public async Task Deleting_a_child_with_leave_against_it_is_refused()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = profile.UserId,
            EmployeeProfileId = profile.Id,
            ChildId = created.Value!.Id,
            StartDate = new DateTime(2026, 3, 2),
            EndDate = new DateTime(2026, 3, 6),
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        var result = await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value.Id,
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Single(await db.Children.ToListAsync());
    }

    [Fact]
    public async Task A_child_with_no_leave_can_be_removed()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        var result = await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value!.Id,
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.Children.ToListAsync());
    }

    /// <summary>
    /// A child who has passed the eligibility age is still listed — the UI has to
    /// explain why they are ineligible rather than silently dropping them, and their
    /// approved leave is still history.
    /// </summary>
    [Fact]
    public async Task An_aged_out_child_is_listed_as_ineligible()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var bornLongAgo = new UpsertChildRequest { Name = "Petros", DateOfBirth = new DateOnly(2005, 1, 20) };

        var result = await Create(db, profile.UserId, bornLongAgo);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsEligible);
        Assert.Equal(21, result.Value.AgeYears);
    }
}
