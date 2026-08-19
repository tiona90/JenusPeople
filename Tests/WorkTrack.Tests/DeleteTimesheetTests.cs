using System.Security.Claims;
using API.Controllers;
using Application.Core;
using Application.Timesheets.Commands;
using Application.Timesheets.Queries;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// DELETE api/timesheets/{id} applied its own ownership rule inline, and it was
/// narrower than TimesheetAccess: only the employee the timesheet belonged to
/// could delete it. An Admin could not, and neither could the manager responsible
/// for that employee — even though both may add, edit and delete the timesheet's
/// entries through TimesheetEntriesController, which has always used
/// TimesheetAccess. Someone able to empty a timesheet one entry at a time could
/// not remove the timesheet itself.
///
/// It also answered "only Draft timesheets can be deleted" before it worked out
/// who was asking, so an unrelated caller learned that a timesheet existed and
/// what state it was in. Authorization runs first now, which is what
/// <see cref="Authorization_is_decided_before_the_draft_rule"/> pins.
///
/// None of this was covered by a test, which is how the narrow rule survived a
/// refactor that extracted TimesheetAccess specifically to replace it.
/// </summary>
public class DeleteTimesheetTests
{
    private const string OwnerUserId = "owner-u";
    private const string OwnerProfileId = "owner-p";
    private const string DeptManagerUserId = "mgr1-u";
    private const string OtherManagerUserId = "mgr2-u";
    private const string OutsiderUserId = "outsider-u";
    private const string AdminUserId = "admin-u";

    private const string DraftId = "ts-draft";
    private const string ApprovedId = "ts-approved";

    /// <summary>
    /// department 1: owner, managed by mgr1. department 2: outsider, managed by
    /// mgr2, who therefore has no claim on department 1. The owner has a Draft
    /// timesheet with two entries, and an Approved one to test the status rule.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Users.Add(new User { Id = OwnerUserId, UserName = "owner", Email = "owner@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = OwnerProfileId, UserId = OwnerUserId, DepartmentId = 1 });

        db.Users.Add(new User { Id = DeptManagerUserId, UserName = "mgr1", Email = "mgr1@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mgr1-p", UserId = DeptManagerUserId, DepartmentId = 1 });

        db.Users.Add(new User { Id = OtherManagerUserId, UserName = "mgr2", Email = "mgr2@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mgr2-p", UserId = OtherManagerUserId, DepartmentId = 2 });

        db.Users.Add(new User { Id = OutsiderUserId, UserName = "outsider", Email = "outsider@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "outsider-p", UserId = OutsiderUserId, DepartmentId = 2 });

        db.Users.Add(new User { Id = AdminUserId, UserName = "admin", Email = "admin@test.local" });

        AddTimesheet(db, DraftId, TimesheetStatus.Draft);
        AddTimesheet(db, ApprovedId, TimesheetStatus.Approved);

        db.SaveChanges();

        // A restart gets a fresh scope and an empty tracker. Clearing it means the
        // handler has to load what it deletes rather than finding the entries
        // already tracked from seeding.
        db.ChangeTracker.Clear();
        return db;
    }

    private static void AddTimesheet(AppDbContext db, string id, TimesheetStatus status)
    {
        db.Timesheets.Add(new Timesheet
        {
            Id = id,
            EmployeeProfileId = OwnerProfileId,
            DepartmentId = 1,
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 7),
            TotalHours = 8m,
            Status = status,
        });

        for (var i = 1; i <= 2; i++)
        {
            db.TimesheetEntries.Add(new TimesheetEntry
            {
                Id = $"e{i}-{id}",
                TimesheetId = id,
                ProjectId = 1,
                Date = new DateTime(2024, 1, i + 1),
                HoursWorked = 4m,
            });
        }
    }

    private static Task<Result<Unit>> Delete(
        AppDbContext db,
        string timesheetId,
        string userId,
        bool isAdmin = false,
        bool isManager = false) =>
        new DeleteTimesheet.Handler(db).Handle(
            new DeleteTimesheet.Command
            {
                Id = timesheetId,
                RequestingUserId = userId,
                IsAdmin = isAdmin,
                IsManager = isManager,
            },
            CancellationToken.None);

    [Fact]
    public async Task The_owner_can_delete_their_own_draft()
    {
        using var db = SeedWorld();

        var result = await Delete(db, DraftId, OwnerUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(await db.Timesheets.FirstOrDefaultAsync(t => t.Id == DraftId));
    }

    /// <summary>
    /// The widening. Both of these were refused outright before, despite either
    /// being free to delete the timesheet's entries one at a time.
    /// </summary>
    [Theory]
    [InlineData(AdminUserId, true, false)]
    [InlineData(DeptManagerUserId, false, true)]
    public async Task An_admin_and_the_responsible_manager_can_delete_it_too(
        string userId, bool isAdmin, bool isManager)
    {
        using var db = SeedWorld();

        var result = await Delete(db, DraftId, userId, isAdmin, isManager);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(await db.Timesheets.FirstOrDefaultAsync(t => t.Id == DraftId));
    }

    /// <summary>
    /// Widening the rule must not widen it to everybody: a manager outside the
    /// employee's scope, and an unrelated employee, are still refused.
    /// </summary>
    [Theory]
    [InlineData(OutsiderUserId, false, false)]
    [InlineData(OtherManagerUserId, false, true)]
    public async Task Someone_with_no_claim_on_it_is_refused(string userId, bool isAdmin, bool isManager)
    {
        using var db = SeedWorld();

        var result = await Delete(db, DraftId, userId, isAdmin, isManager);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
        Assert.NotNull(await db.Timesheets.FirstOrDefaultAsync(t => t.Id == DraftId));
    }

    [Fact]
    public async Task A_timesheet_that_does_not_exist_is_not_found()
    {
        using var db = SeedWorld();

        var result = await Delete(db, "no-such-timesheet", AdminUserId, isAdmin: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    /// <summary>
    /// A state refusal is a Conflict, matching how deleting a referenced
    /// department, project or leave type already reports. It used to be a 400 with
    /// a bare string body, which the client's error reader could not see into — so
    /// the user got the generic "Failed to delete timesheet." instead of the reason.
    /// </summary>
    [Fact]
    public async Task A_timesheet_past_draft_cannot_be_deleted()
    {
        using var db = SeedWorld();

        var result = await Delete(db, ApprovedId, OwnerUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal("Only Draft timesheets can be deleted.", result.Error);
        Assert.NotNull(await db.Timesheets.FirstOrDefaultAsync(t => t.Id == ApprovedId));
    }

    /// <summary>
    /// The ordering half of the fix. An outsider naming an Approved timesheet must
    /// be told it is not theirs — not that it is past Draft, which would confirm
    /// the timesheet exists and disclose its state.
    /// </summary>
    [Fact]
    public async Task Authorization_is_decided_before_the_draft_rule()
    {
        using var db = SeedWorld();

        var result = await Delete(db, ApprovedId, OutsiderUserId);

        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
        Assert.DoesNotContain("Draft", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_a_timesheet_takes_its_entries_with_it()
    {
        using var db = SeedWorld();

        var result = await Delete(db, DraftId, OwnerUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(await db.TimesheetEntries.Where(e => e.TimesheetId == DraftId).ToListAsync());

        // The other timesheet's entries are untouched.
        Assert.Equal(2, await db.TimesheetEntries.CountAsync(e => e.TimesheetId == ApprovedId));
    }

    private static ServiceProvider BuildProvider(AppDbContext db) =>
        new ServiceCollection()
            .AddSingleton(db)
            // MediatR 13's license accessor resolves ILoggerFactory during construction.
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetTimesheetDetail>())
            .BuildServiceProvider();

    private static TimesheetsController ControllerFor(IServiceProvider provider, string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        };

        // Deleting does not touch the notifications hub; a null here fails loudly if
        // that ever stops being true.
        return new TimesheetsController(provider.GetRequiredService<AppDbContext>(), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static int StatusOf(ActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        // A refusal is a refusal however it is expressed, so these tests read
        // ForbidResult as 403. It is not equivalent in every respect — a bare
        // Forbid() invokes the authentication scheme's challenge and carries no
        // body, where HandleResult answers with an explicit 403 and a reason the
        // client can display — but which of the two is returned is response
        // plumbing, not the authorization rule these cases are about.
        ForbidResult => StatusCodes.Status403Forbidden,
        _ => 0,
    };

    /// <summary>
    /// Pins the wiring, not the rule: the action has to pass the caller's real id
    /// and role flags. Hardcoding IsAdmin false — or dropping IsManager, as the
    /// inline version effectively did — would re-narrow the rule without touching
    /// the handler these other tests exercise.
    /// </summary>
    [Theory]
    [InlineData(OwnerUserId, AppRoles.Employee, StatusCodes.Status200OK)]
    [InlineData(AdminUserId, AppRoles.Admin, StatusCodes.Status200OK)]
    [InlineData(DeptManagerUserId, AppRoles.Manager, StatusCodes.Status200OK)]
    [InlineData(OutsiderUserId, AppRoles.Employee, StatusCodes.Status403Forbidden)]
    [InlineData(OtherManagerUserId, AppRoles.Manager, StatusCodes.Status403Forbidden)]
    public async Task The_endpoint_honours_the_shared_rule(string userId, string role, int expectedStatus)
    {
        using var db = SeedWorld();
        using var provider = BuildProvider(db);

        var response = await ControllerFor(provider, userId, role)
            .DeleteTimesheet(DraftId, CancellationToken.None);

        Assert.Equal(expectedStatus, StatusOf(response));
    }

    [Fact]
    public async Task The_endpoint_reports_a_state_refusal_as_a_conflict()
    {
        using var db = SeedWorld();
        using var provider = BuildProvider(db);

        var response = await ControllerFor(provider, OwnerUserId, AppRoles.Employee)
            .DeleteTimesheet(ApprovedId, CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(response));
    }
}
