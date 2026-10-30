using System.Security.Claims;
using API.Controllers;
using Application.Core;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// An entry write is two saves — the entry, then the sheet's TotalHours — and they
/// ran unprotected: a failure on the second left the entry written and the total
/// describing the sheet as it was. A concurrency conflict on the sheet (another tab
/// logging time, a manager deciding the sheet) surfaced as a raw 500 and an email
/// to every System Administrator, and AddEntry answered a database error with its
/// own 500 carrying the provider's message — table and constraint names included —
/// which also kept it out of the System Log. Both saves now share a transaction, a
/// conflict answers 409, and a genuine fault goes to the error middleware.
///
/// On SQLite: the in-memory provider ignores transactions (see TransactionalTestDb).
/// </summary>
public class TimesheetEntryWriteAtomicityTests
{
    private const string TimesheetId = "ts";

    private sealed class ThrowOnSave(int n, Func<Exception> failure) : SaveChangesInterceptor
    {
        private int _count;
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && ++_count == n) throw failure();
            return ValueTask.FromResult(result);
        }
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = 1, Name = "Apollo", Code = "APL", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });
        db.Projects.Add(new Project { Id = 2, Name = "Borealis", Code = "BOR", DepartmentAssignments = { new ProjectDepartment { DepartmentId = 1 } } });
        db.Users.Add(new User { Id = "u1", UserName = "u1", Email = "u1@t.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = 1 });
        db.Timesheets.Add(new Timesheet
        {
            Id = TimesheetId, EmployeeProfileId = "p1", DepartmentId = 1, Status = TimesheetStatus.Draft, TotalHours = 8,
            PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7),
        });
        db.TimesheetEntries.Add(new TimesheetEntry { Id = "e1", TimesheetId = TimesheetId, ProjectId = 1, Date = new DateTime(2024, 1, 2), HoursWorked = 8 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static TimesheetEntriesController Controller(AppDbContext db) => new(db)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "u1"), new Claim(ClaimTypes.Role, AppRoles.Employee)], "Test")),
            },
        },
    };

    private static CreateEntryRequest NewEntry() => new() { ProjectId = 2, Date = new DateTime(2024, 1, 2), HoursWorked = 4 };

    [Fact]
    public async Task A_failed_total_write_rolls_back_the_entry()
    {
        var interceptor = new ThrowOnSave(2, () => new InvalidOperationException("total write failed"));
        await using var db = await TransactionalTestDb.CreateAsync(interceptor);
        await SeedAsync(db);
        interceptor.Armed = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Controller(db).AddEntry(TimesheetId, NewEntry(), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.TimesheetEntries.CountAsync());
        Assert.Equal(8m, (await db.Timesheets.AsNoTracking().SingleAsync()).TotalHours);
    }

    [Fact]
    public async Task The_total_is_kept_in_step_with_the_entries()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        var result = await Controller(db).AddEntry(TimesheetId, NewEntry(), CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        db.ChangeTracker.Clear();
        Assert.Equal(12m, (await db.Timesheets.AsNoTracking().SingleAsync()).TotalHours);
    }

    [Fact]
    public async Task A_concurrency_conflict_answers_409_and_writes_nothing()
    {
        var interceptor = new ThrowOnSave(2, () => new DbUpdateConcurrencyException("the sheet moved"));
        await using var db = await TransactionalTestDb.CreateAsync(interceptor);
        await SeedAsync(db);
        interceptor.Armed = true;

        var result = await Controller(db).AddEntry(TimesheetId, NewEntry(), CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.TimesheetEntries.CountAsync());
    }

    [Fact]
    public async Task A_database_error_is_not_echoed_back_to_the_caller()
    {
        var interceptor = new ThrowOnSave(1, () => new DbUpdateException("FK_TimesheetEntries_Projects violated on dbo.TimesheetEntries"));
        await using var db = await TransactionalTestDb.CreateAsync(interceptor);
        await SeedAsync(db);
        interceptor.Armed = true;

        // Thrown to GlobalExceptionMiddleware — a 500 with no details, logged and
        // recorded in the System Log — rather than answered here with the message.
        await Assert.ThrowsAsync<DbUpdateException>(() => Controller(db).AddEntry(TimesheetId, NewEntry(), CancellationToken.None));
    }
}
