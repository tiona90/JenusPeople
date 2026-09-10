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
}
