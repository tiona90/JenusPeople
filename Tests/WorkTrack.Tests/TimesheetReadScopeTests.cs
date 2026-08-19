using System.Security.Claims;
using API.Controllers;
using Application.Core;
using Application.Timesheets.Queries;
using Application.TimesheetStatusHistories.DTOs;
using Application.TimesheetStatusHistories.Queries;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// GET /api/timesheets/{id} and /api/timesheets/{id}/history used to be unscoped
/// lookups behind a bare [Authorize]: any signed-in user could read any
/// employee's hours, and their whole approval trail, by id. Both now run through
/// the same scope filter the list endpoints use, so an out-of-scope caller sees
/// a timesheet that does not exist for them.
///
/// The handler tests pin the rule; the controller tests at the end pin the
/// wiring, since passing the wrong RequestingUserId or a hardcoded IsAdmin would
/// reopen the hole without touching the rule itself.
/// </summary>
public class TimesheetReadScopeTests
{
    private const string OwnerUserId = "owner-u";
    private const string OutsiderUserId = "outsider-u";
    private const string DeptManagerUserId = "mgr1-u";
    private const string OtherManagerUserId = "mgr2-u";
    private const string AdminUserId = "admin-u";

    private const string OwnerTimesheetId = "ts-owner";
    private const string ReportTimesheetId = "ts-report";

    /// <summary>
    /// department 1: owner, managed by mgr1.
    /// department 2: outsider, managed by mgr2 — no claim on department 1.
    /// department 3: report, whose ManagerId points at mgr1 (a direct report
    ///               outside mgr1's own department).
    /// Each of owner and report has one timesheet with one status-history row.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Users.Add(new User { Id = OwnerUserId, UserName = "owner", Email = "owner@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "owner-p", UserId = OwnerUserId, DepartmentId = 1 });

        db.Users.Add(new User { Id = DeptManagerUserId, UserName = "mgr1", Email = "mgr1@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mgr1-p", UserId = DeptManagerUserId, DepartmentId = 1 });

        db.Users.Add(new User { Id = OutsiderUserId, UserName = "outsider", Email = "outsider@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "outsider-p", UserId = OutsiderUserId, DepartmentId = 2 });

        db.Users.Add(new User { Id = OtherManagerUserId, UserName = "mgr2", Email = "mgr2@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mgr2-p", UserId = OtherManagerUserId, DepartmentId = 2 });

        db.Users.Add(new User { Id = "report-u", UserName = "report", Email = "report@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "report-p", UserId = "report-u", DepartmentId = 3, ManagerId = "mgr1-p" });

        db.Users.Add(new User { Id = AdminUserId, UserName = "admin", Email = "admin@test.local" });

        AddTimesheet(db, OwnerTimesheetId, "owner-p", departmentId: 1, historyId: "h-owner");
        AddTimesheet(db, ReportTimesheetId, "report-p", departmentId: 3, historyId: "h-report");

        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static void AddTimesheet(AppDbContext db, string id, string employeeProfileId, int departmentId, string historyId)
    {
        db.Timesheets.Add(new Timesheet
        {
            Id = id,
            EmployeeId = employeeProfileId,
            DepartmentId = departmentId,
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 7),
            TotalHours = 8m,
            Status = TimesheetStatus.Approved,
        });
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            Id = $"e-{id}",
            TimesheetId = id,
            ProjectId = 1,
            Date = new DateTime(2024, 1, 2),
            HoursWorked = 8m,
        });
        db.TimesheetStatusHistories.Add(new TimesheetStatusHistory
        {
            Id = historyId,
            TimesheetId = id,
            ChangedByUserId = AdminUserId,
            FromStatus = (int)TimesheetStatus.Submitted,
            ToStatus = (int)TimesheetStatus.Approved,
            ChangedAt = new DateTime(2024, 1, 8),
        });
    }

    private static Task<Result<Timesheet>> ReadTimesheet(
        AppDbContext db, string timesheetId, string userId, bool isAdmin = false, bool isManager = false) =>
        new GetTimesheetDetail.Handler(db).Handle(
            new GetTimesheetDetail.Query
            {
                Id = timesheetId,
                RequestingUserId = userId,
                IsAdmin = isAdmin,
                IsManager = isManager,
            },
            CancellationToken.None);

    private static async Task<List<TimesheetStatusHistoryDto>> ReadHistory(
        AppDbContext db, string timesheetId, string userId, bool isAdmin = false, bool isManager = false)
    {
        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(
            new GetTimesheetStatusHistoryList.Query
            {
                TimesheetId = timesheetId,
                RequestingUserId = userId,
                IsAdmin = isAdmin,
                IsManager = isManager,
            },
            CancellationToken.None);

        return result.Items.ToList();
    }

    // Reading one timesheet by id

    [Fact]
    public async Task Owner_can_read_their_own_timesheet_entries_included()
    {
        using var db = SeedWorld();

        var result = await ReadTimesheet(db, OwnerTimesheetId, OwnerUserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(OwnerTimesheetId, result.Value!.Id);
        // The endpoint's clients read entries off this payload; the scoping rewrite
        // must not have dropped the Include.
        Assert.Single(result.Value.Entries);
    }

    [Fact]
    public async Task An_unrelated_employee_cannot_read_someone_elses_timesheet()
    {
        using var db = SeedWorld();

        var result = await ReadTimesheet(db, OwnerTimesheetId, OutsiderUserId);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        // Reported as "not found" rather than "forbidden" so the response does not
        // confirm that another employee's timesheet exists.
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task A_manager_can_read_a_timesheet_in_their_own_department()
    {
        using var db = SeedWorld();

        var result = await ReadTimesheet(db, OwnerTimesheetId, DeptManagerUserId, isManager: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(OwnerTimesheetId, result.Value!.Id);
    }

    [Fact]
    public async Task A_manager_can_read_a_direct_reports_timesheet_in_another_department()
    {
        using var db = SeedWorld();

        var result = await ReadTimesheet(db, ReportTimesheetId, DeptManagerUserId, isManager: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(ReportTimesheetId, result.Value!.Id);
    }

    [Fact]
    public async Task A_manager_cannot_read_a_timesheet_outside_their_scope()
    {
        using var db = SeedWorld();

        // mgr2 runs department 2 and manages nobody in department 1.
        var result = await ReadTimesheet(db, OwnerTimesheetId, OtherManagerUserId, isManager: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
    }

    [Fact]
    public async Task An_admin_can_read_any_timesheet()
    {
        using var db = SeedWorld();

        Assert.True((await ReadTimesheet(db, OwnerTimesheetId, AdminUserId, isAdmin: true)).IsSuccess);
        Assert.True((await ReadTimesheet(db, ReportTimesheetId, AdminUserId, isAdmin: true)).IsSuccess);
    }

    [Fact]
    public async Task A_timesheet_that_does_not_exist_is_not_found_even_for_an_admin()
    {
        using var db = SeedWorld();

        var result = await ReadTimesheet(db, "no-such-timesheet", AdminUserId, isAdmin: true);

        Assert.False(result.IsSuccess);
    }

    // Reading one timesheet's status history

    [Fact]
    public async Task Owner_sees_the_history_of_their_own_timesheet()
    {
        using var db = SeedWorld();

        var history = await ReadHistory(db, OwnerTimesheetId, OwnerUserId);

        Assert.Equal(["h-owner"], history.Select(h => h.Id));
    }

    [Fact]
    public async Task An_unrelated_employee_sees_no_history_for_someone_elses_timesheet()
    {
        using var db = SeedWorld();

        var history = await ReadHistory(db, OwnerTimesheetId, OutsiderUserId);

        Assert.Empty(history);
    }

    [Fact]
    public async Task A_manager_sees_history_inside_their_scope_but_not_outside_it()
    {
        using var db = SeedWorld();

        Assert.Equal(
            ["h-owner"],
            (await ReadHistory(db, OwnerTimesheetId, DeptManagerUserId, isManager: true)).Select(h => h.Id));

        Assert.Empty(await ReadHistory(db, OwnerTimesheetId, OtherManagerUserId, isManager: true));
    }

    [Fact]
    public async Task An_admin_sees_the_history_of_any_timesheet()
    {
        using var db = SeedWorld();

        Assert.Equal(
            ["h-report"],
            (await ReadHistory(db, ReportTimesheetId, AdminUserId, isAdmin: true)).Select(h => h.Id));
    }

    [Fact]
    public async Task The_timesheet_filter_keeps_other_timesheets_history_out()
    {
        using var db = SeedWorld();

        // An admin sees everything, so only the TimesheetId filter can narrow this.
        var history = await ReadHistory(db, OwnerTimesheetId, AdminUserId, isAdmin: true);

        Assert.Equal(["h-owner"], history.Select(h => h.Id));
    }

    // Through the controller, over a real MediatR pipeline

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

        // Neither read action touches the notifications hub; a null here fails loudly
        // if that ever stops being true.
        return new TimesheetsController(provider.GetRequiredService<AppDbContext>(), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static int StatusOf(IActionResult result) => result switch
    {
        ObjectResult objectResult => objectResult.StatusCode ?? StatusCodes.Status200OK,
        StatusCodeResult statusCodeResult => statusCodeResult.StatusCode,
        _ => 0,
    };

    [Fact]
    public async Task GetTimesheet_returns_the_timesheet_to_its_owner_and_404s_an_outsider()
    {
        using var db = SeedWorld();
        using var provider = BuildProvider(db);

        var asOwner = await ControllerFor(provider, OwnerUserId, AppRoles.Employee)
            .GetTimesheet(OwnerTimesheetId, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, StatusOf(asOwner.Result!));
        Assert.Equal(OwnerTimesheetId, Assert.IsType<Timesheet>(((ObjectResult)asOwner.Result!).Value).Id);

        var asOutsider = await ControllerFor(provider, OutsiderUserId, AppRoles.Employee)
            .GetTimesheet(OwnerTimesheetId, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(asOutsider.Result!));
    }

    [Fact]
    public async Task GetTimesheet_serves_an_admin_and_the_departments_manager()
    {
        using var db = SeedWorld();
        using var provider = BuildProvider(db);

        var asAdmin = await ControllerFor(provider, AdminUserId, AppRoles.Admin)
            .GetTimesheet(OwnerTimesheetId, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, StatusOf(asAdmin.Result!));

        var asManager = await ControllerFor(provider, DeptManagerUserId, AppRoles.Manager)
            .GetTimesheet(OwnerTimesheetId, CancellationToken.None);
        Assert.Equal(StatusCodes.Status200OK, StatusOf(asManager.Result!));

        var asOtherManager = await ControllerFor(provider, OtherManagerUserId, AppRoles.Manager)
            .GetTimesheet(OwnerTimesheetId, CancellationToken.None);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(asOtherManager.Result!));
    }

    [Fact]
    public async Task GetStatusHistory_serves_the_owner_and_withholds_from_an_outsider()
    {
        using var db = SeedWorld();
        using var provider = BuildProvider(db);

        var asOwner = await ControllerFor(provider, OwnerUserId, AppRoles.Employee)
            .GetStatusHistory(OwnerTimesheetId, CancellationToken.None);
        var owned = Assert.IsAssignableFrom<IEnumerable<TimesheetStatusHistoryDto>>(((ObjectResult)asOwner).Value);
        Assert.Equal(["h-owner"], owned.Select(h => h.Id));

        var asOutsider = await ControllerFor(provider, OutsiderUserId, AppRoles.Employee)
            .GetStatusHistory(OwnerTimesheetId, CancellationToken.None);
        var seen = Assert.IsAssignableFrom<IEnumerable<TimesheetStatusHistoryDto>>(((ObjectResult)asOutsider).Value);
        Assert.Empty(seen);
    }
}
