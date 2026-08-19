using Application.Core;
using Application.Timesheets.Commands;
using Application.Timesheets.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// CreateTimesheet reserves nothing before inserting, so two calls for the same
/// period could both build a timesheet and both write it: one employee, one week,
/// two draft timesheets, and every later query having to guess which is real.
///
/// A unique index on (EmployeeId, PeriodStart) is the only place that can settle
/// it — a check in the handler is always a race — and the handler now reports the
/// resulting write failure as 409 rather than letting it surface as a 500.
///
/// These need a database that enforces the index, which the in-memory provider
/// does not: see <see cref="TransactionalTestDb"/>.
/// </summary>
public class DuplicateTimesheetTests
{
    private const string UserId = "u-employee";
    private const string ProfileId = "p-employee";
    private const string OtherUserId = "u-colleague";
    private const string OtherProfileId = "p-colleague";

    // Monday to Friday.
    private static readonly DateTime PeriodStart = new(2024, 3, 4);
    private static readonly DateTime PeriodEnd = new(2024, 3, 8);

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Users.Add(new User
        {
            Id = UserId,
            UserName = "employee@test.local",
            Email = "employee@test.local",
            DisplayName = "Employee",
        });
        db.Users.Add(new User
        {
            Id = OtherUserId,
            UserName = "colleague@test.local",
            Email = "colleague@test.local",
            DisplayName = "Colleague",
        });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = ProfileId, UserId = UserId, DepartmentId = 1 });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = OtherProfileId, UserId = OtherUserId, DepartmentId = 1 });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static CreateTimesheet.Command CreateFor(string userId, DateTime start, DateTime end) => new()
    {
        RequestingUserId = userId,
        PeriodStart = start,
        PeriodEnd = end,
    };

    private static Task<Result<TimesheetDto>> Create(AppDbContext db, CreateTimesheet.Command command) =>
        new CreateTimesheet.Handler(db).Handle(command, CancellationToken.None);

    [Fact]
    public async Task Two_creates_for_the_same_period_yield_one_success_and_one_conflict()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        // Two contexts, as two simultaneous requests would have: separate change
        // trackers over one database. The loser of the race is the one whose insert
        // reaches the database second, which is what this stages — deliberately,
        // rather than starting two tasks and hoping the timing lands.
        await using var second = TransactionalTestDb.Attach(db);

        var winner = await Create(db, CreateFor(UserId, PeriodStart, PeriodEnd));
        var loser = await Create(second, CreateFor(UserId, PeriodStart, PeriodEnd));

        Assert.True(winner.IsSuccess, winner.Error);

        Assert.False(loser.IsSuccess);
        // 409, not the 500 an unhandled DbUpdateException produced.
        Assert.Equal(ResultErrorKind.Conflict, loser.ErrorKind);
        Assert.Contains("already exists", loser.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Timesheets.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// The same refusal for a caller who simply asks twice. Before the index this
    /// was the sequential version of the same bug — a second draft, silently.
    /// </summary>
    [Fact]
    public async Task Asking_twice_on_one_context_is_also_a_conflict()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        Assert.True((await Create(db, CreateFor(UserId, PeriodStart, PeriodEnd))).IsSuccess);

        var again = await Create(db, CreateFor(UserId, PeriodStart, PeriodEnd));

        Assert.False(again.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, again.ErrorKind);

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.Timesheets.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// The index covers (EmployeeId, PeriodStart), so it must not stand in the way
    /// of the next week — the ordinary case, and what an index on EmployeeId alone
    /// would have broken.
    /// </summary>
    [Fact]
    public async Task The_next_period_is_still_allowed()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        Assert.True((await Create(db, CreateFor(UserId, PeriodStart, PeriodEnd))).IsSuccess);

        var nextWeek = await Create(db, CreateFor(UserId, PeriodStart.AddDays(7), PeriodEnd.AddDays(7)));

        Assert.True(nextWeek.IsSuccess, nextWeek.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Timesheets.AsNoTracking().CountAsync());
    }

    /// <summary>
    /// And it is scoped per employee: a whole department filing the same week must
    /// not collide, which a unique index on PeriodStart alone would have caused.
    /// </summary>
    [Fact]
    public async Task A_colleague_may_file_the_same_period()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        Assert.True((await Create(db, CreateFor(UserId, PeriodStart, PeriodEnd))).IsSuccess);

        var colleague = await Create(db, CreateFor(OtherUserId, PeriodStart, PeriodEnd));

        Assert.True(colleague.IsSuccess, colleague.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Timesheets.AsNoTracking().CountAsync());
    }
}
