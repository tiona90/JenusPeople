using Application.Core;
using Application.Departments.Commands;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// DeleteDepartment refuses a department that something still references, and
/// says what to reassign. The refusal is only as good as the count behind it:
/// anything the pre-check cannot see becomes a foreign-key violation on
/// SaveChanges, which leaves the middleware nothing better to answer than 500
/// "An unexpected server error occurred." — the shape of the bug an admin hit on
/// the deployed site while the same delete succeeded locally, because only the
/// deployed database had the referencing rows.
///
/// The database holds five foreign keys into Departments. The handler counted
/// three of them, and two of those three were narrowed by a global query filter,
/// so they under-counted as well:
///
/// <list type="bullet">
/// <item>EmployeeProfiles — counted, but the soft-delete filter hides a leaver's
/// row, which the foreign key still enforces. This is why a department reading
/// "0 people" could refuse to delete.</item>
/// <item>UserDepartments — counted, unfiltered.</item>
/// <item>ProjectDepartments — counted, but filtered on the project's soft-delete
/// the same way.</item>
/// <item>Timesheets — not counted at all. A timesheet keeps the department it was
/// filed under, so it outlives the employee's move to another one.</item>
/// <item>AnnualLeaves — not counted at all.</item>
/// </list>
///
/// These need a database that enforces foreign keys, which the EF in-memory
/// provider does not: see <see cref="TransactionalTestDb"/>. Under
/// <see cref="TestDb"/> every one of them would pass without the handler doing
/// anything.
/// </summary>
public class DeleteDepartmentBlockerTests
{
    private const int TargetId = 2;
    private const int OtherId = 1;

    /// <summary>
    /// Two departments: the one under test, and somewhere for a parent row that
    /// must not itself be a blocker to live.
    /// </summary>
    private static async Task SeedAsync(AppDbContext db)
    {
        db.Departments.Add(new Department { Id = OtherId, Name = "Engineering", Code = "ENG" });
        db.Departments.Add(new Department { Id = TargetId, Name = "Finance", Code = "FIN" });
        db.Users.Add(new User
        {
            Id = "u-leaver",
            UserName = "leaver@test.local",
            Email = "leaver@test.local",
            DisplayName = "Leaver",
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static Task<Result<MediatR.Unit>> Delete(AppDbContext db) =>
        new DeleteDepartment.Handler(db).Handle(
            new DeleteDepartment.Command { Id = TargetId },
            CancellationToken.None);

    /// <summary>
    /// The case from the report: the department shows no people, because the
    /// count that feeds both the card and the pre-check excludes soft-deleted
    /// profiles — but the foreign key does not.
    /// </summary>
    [Fact]
    public async Task A_soft_deleted_employee_profile_is_a_conflict_not_a_500()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "p-leaver",
            UserId = "u-leaver",
            DepartmentId = TargetId,
            IsDeleted = true,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // The filter hides it, so the handler's own count sees nothing.
        Assert.Equal(0, await db.EmployeeProfiles.CountAsync(p => p.DepartmentId == TargetId));
        Assert.Equal(1, await db.EmployeeProfiles.IgnoreQueryFilters()
            .CountAsync(p => p.DepartmentId == TargetId));

        var result = await Delete(db);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("Finance", result.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Departments.CountAsync());
    }

    /// <summary>
    /// A timesheet carries the department it was filed under, so it keeps
    /// referencing Finance long after its author moved to Engineering — and it
    /// was not counted at all.
    /// </summary>
    [Fact]
    public async Task A_timesheet_filed_under_the_department_is_a_conflict_not_a_500()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "p-mover",
            UserId = "u-leaver",
            DepartmentId = OtherId,
        });
        db.Timesheets.Add(new Timesheet
        {
            Id = "t-old",
            EmployeeProfileId = "p-mover",
            DepartmentId = TargetId,
            PeriodStart = new DateTime(2024, 3, 4),
            PeriodEnd = new DateTime(2024, 3, 8),
            TotalHours = 40m,
            Status = TimesheetStatus.Approved,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Delete(db);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("timesheet", result.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Departments.CountAsync());
    }

    /// <summary>A leave request stamped with the department, likewise uncounted.</summary>
    [Fact]
    public async Task A_leave_request_against_the_department_is_a_conflict_not_a_500()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "l-old",
            EmployeeId = "u-leaver",
            DepartmentId = TargetId,
            StartDate = new DateTime(2024, 3, 4),
            EndDate = new DateTime(2024, 3, 5),
            Status = AnnualLeaveStatus.Approved,
            CreatedAt = new DateTime(2024, 3, 1),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Delete(db);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("leave request", result.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Departments.CountAsync());
    }

    /// <summary>
    /// The project count is filtered on the project's own soft-delete, so an
    /// assignment belonging to a deleted project is invisible to it while the
    /// foreign key still holds.
    /// </summary>
    [Fact]
    public async Task An_assignment_of_a_soft_deleted_project_is_a_conflict_not_a_500()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        db.Projects.Add(new Project { Id = 7, Name = "Apollo", Code = "APL", IsDeleted = true });
        db.ProjectDepartments.Add(new ProjectDepartment { ProjectId = 7, DepartmentId = TargetId });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(0, await db.ProjectDepartments.CountAsync(pd => pd.DepartmentId == TargetId));

        var result = await Delete(db);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);

        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Departments.CountAsync());
    }

    /// <summary>
    /// The ordinary case still has to work: nothing references it, it goes. A
    /// pre-check widened until it refuses everything would "fix" the 500 by
    /// breaking the feature.
    /// </summary>
    [Fact]
    public async Task An_unreferenced_department_still_deletes()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);

        // A live profile and a timesheet in the *other* department, so the counts
        // have rows to ignore rather than an empty table.
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "p-elsewhere",
            UserId = "u-leaver",
            DepartmentId = OtherId,
        });
        db.Timesheets.Add(new Timesheet
        {
            Id = "t-elsewhere",
            EmployeeProfileId = "p-elsewhere",
            DepartmentId = OtherId,
            PeriodStart = new DateTime(2024, 3, 4),
            PeriodEnd = new DateTime(2024, 3, 8),
            TotalHours = 40m,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Delete(db);

        Assert.True(result.IsSuccess, result.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(OtherId, (await db.Departments.SingleAsync()).Id);
    }
}
