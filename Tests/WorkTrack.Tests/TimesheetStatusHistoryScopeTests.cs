using System.Security.Claims;
using API.Controllers;
using Application.Core;
using Application.TimesheetStatusHistories.DTOs;
using Application.TimesheetStatusHistories.Queries;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Regression for the manager-scope gap in GetTimesheetStatusHistoryList: the
/// IsManager branch previously had no department filter (a Manager saw every
/// timesheet's status history). A manager must only see history for their own
/// timesheets, their managed departments, and their direct reports.
///
/// The EmployeeProfileId tests cover the filter added so that
/// GET /api/employees/{employeeProfileId}/timesheets/history could stop
/// hand-rolling its own authorization. That endpoint compared
/// User.Identity.Name — an email — against an EmployeeProfile.Id, which no
/// non-admin could ever match, so it answered every one of them with a 403. The
/// controller tests at the end pin the wiring, since the filter is only as good
/// as the scope flags the action passes with it.
/// </summary>
public class TimesheetStatusHistoryScopeTests
{
    private const string ManagerUserId = "mgr-u";

    private static void SeedWorld(AppDbContext db)
    {
        // Manager in department 1.
        db.Users.Add(new User { Id = ManagerUserId, UserName = "mgr", Email = "mgr@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mgr-p", UserId = ManagerUserId, DepartmentId = 1 });

        // Employee in the manager's department (in scope).
        db.Users.Add(new User { Id = "in-u", UserName = "in", Email = "in@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "in-p", UserId = "in-u", DepartmentId = 1, ManagerId = "mgr-p" });

        // Employee in a different department, not a report (out of scope).
        db.Users.Add(new User { Id = "out-u", UserName = "out", Email = "out@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "out-p", UserId = "out-u", DepartmentId = 2 });

        db.Timesheets.Add(new Timesheet { Id = "ts-in", EmployeeProfileId = "in-p", DepartmentId = 1, PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7), Status = TimesheetStatus.Approved });
        db.Timesheets.Add(new Timesheet { Id = "ts-out", EmployeeProfileId = "out-p", DepartmentId = 2, PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7), Status = TimesheetStatus.Approved });

        // Explicit, distinct ChangedAt values and one differing transition, so the
        // date-range and status filters have something to actually discriminate on.
        db.TimesheetStatusHistories.Add(new TimesheetStatusHistory { Id = "h-in", TimesheetId = "ts-in", ChangedByUserId = ManagerUserId, FromStatus = 1, ToStatus = 2, ChangedAt = new DateTime(2024, 1, 10, 0, 0, 0, DateTimeKind.Utc) });
        db.TimesheetStatusHistories.Add(new TimesheetStatusHistory { Id = "h-out", TimesheetId = "ts-out", ChangedByUserId = ManagerUserId, FromStatus = 1, ToStatus = 2, ChangedAt = new DateTime(2024, 2, 20, 0, 0, 0, DateTimeKind.Utc) });
        db.TimesheetStatusHistories.Add(new TimesheetStatusHistory { Id = "h-out-2", TimesheetId = "ts-out", ChangedByUserId = ManagerUserId, FromStatus = 2, ToStatus = 3, ChangedAt = new DateTime(2024, 3, 5, 0, 0, 0, DateTimeKind.Utc) });

        db.SaveChanges();
    }

    [Fact]
    public async Task Manager_only_sees_history_within_their_scope()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(
            new GetTimesheetStatusHistoryList.Query
            {
                RequestingUserId = ManagerUserId,
                IsAdmin = false,
                IsManager = true,
            },
            CancellationToken.None);

        var timesheetIds = result.Items.Select(h => h.TimesheetId).ToList();
        Assert.Contains("ts-in", timesheetIds);
        Assert.DoesNotContain("ts-out", timesheetIds); // outside the manager's department
    }

    [Fact]
    public async Task Admin_sees_all_history()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(
            new GetTimesheetStatusHistoryList.Query
            {
                RequestingUserId = "someone",
                IsAdmin = true,
            },
            CancellationToken.None);

        var timesheetIds = result.Items.Select(h => h.TimesheetId).ToList();
        Assert.Contains("ts-in", timesheetIds);
        Assert.Contains("ts-out", timesheetIds);
    }

    private static async Task<List<string>> HistoryFor(
        AppDbContext db,
        string requestingUserId,
        string? employeeProfileId,
        bool isAdmin = false,
        bool isManager = false)
    {
        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(
            new GetTimesheetStatusHistoryList.Query
            {
                EmployeeProfileId = employeeProfileId,
                RequestingUserId = requestingUserId,
                IsAdmin = isAdmin,
                IsManager = isManager,
            },
            CancellationToken.None);

        return result.Items.Select(h => h.TimesheetId).ToList();
    }

    [Fact]
    public async Task Filtering_by_employee_narrows_to_that_profiles_history()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var timesheetIds = await HistoryFor(db, "someone", "in-p", isAdmin: true);

        Assert.Equal(["ts-in"], timesheetIds);
    }

    /// <summary>
    /// The filter narrows; it must never widen. Naming a profile outside the
    /// caller's scope has to return nothing rather than reaching past the scope
    /// filter to fetch it.
    /// </summary>
    [Fact]
    public async Task Filtering_by_employee_does_not_widen_a_managers_scope()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var timesheetIds = await HistoryFor(db, ManagerUserId, "out-p", isManager: true);

        Assert.Empty(timesheetIds);
    }

    [Fact]
    public async Task An_employee_can_read_their_own_history_by_profile_id()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var timesheetIds = await HistoryFor(db, "in-u", "in-p");

        Assert.Equal(["ts-in"], timesheetIds);
    }

    [Fact]
    public async Task An_employee_cannot_read_another_employees_history()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var timesheetIds = await HistoryFor(db, "in-u", "out-p");

        Assert.Empty(timesheetIds);
    }

    private static async Task<PagedResult<TimesheetStatusHistoryDto>> AdminQuery(
        AppDbContext db,
        Action<GetTimesheetStatusHistoryList.Query> configure)
    {
        var query = new GetTimesheetStatusHistoryList.Query
        {
            RequestingUserId = "admin-u",
            IsAdmin = true,
        };
        configure(query);

        return await new GetTimesheetStatusHistoryList.Handler(db).Handle(query, CancellationToken.None);
    }

    [Fact]
    public async Task Filtering_by_department_narrows_to_that_departments_timesheets()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var inDept1 = await AdminQuery(db, q => q.DepartmentId = 1);
        var inDept2 = await AdminQuery(db, q => q.DepartmentId = 2);

        Assert.Equal(["ts-in"], inDept1.Items.Select(h => h.TimesheetId));
        Assert.Equal(["ts-out"], inDept2.Items.Select(h => h.TimesheetId).Distinct());
    }

    [Fact]
    public async Task Filtering_by_date_range_narrows_on_when_the_change_was_recorded()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        // h-in is 2024-01-10, h-out 2024-02-20, h-out-2 2024-03-05.
        var fromFebruary = await AdminQuery(db, q => q.From = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var untilJanuary = await AdminQuery(db, q => q.To = new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc));
        var februaryOnly = await AdminQuery(db, q =>
        {
            q.From = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            q.To = new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc);
        });

        Assert.Equal(2, fromFebruary.Items.Count);
        Assert.Equal(["h-in"], untilJanuary.Items.Select(h => h.Id));
        Assert.Equal(["h-out"], februaryOnly.Items.Select(h => h.Id));
    }

    [Fact]
    public async Task Filtering_by_status_transition_narrows_on_the_from_and_to_status()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        // h-in and h-out are both Submitted(1) → Approved(2); h-out-2 is 2 → 3.
        var outOfSubmitted = await AdminQuery(db, q => q.FromStatus = (int)TimesheetStatus.Submitted);
        var intoRejected = await AdminQuery(db, q => q.ToStatus = (int)TimesheetStatus.Rejected);
        var bothEnds = await AdminQuery(db, q =>
        {
            q.FromStatus = (int)TimesheetStatus.Approved;
            q.ToStatus = (int)TimesheetStatus.Rejected;
        });

        Assert.Equal(["h-in", "h-out"], outOfSubmitted.Items.Select(h => h.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(["h-out-2"], intoRejected.Items.Select(h => h.Id));
        Assert.Equal(["h-out-2"], bothEnds.Items.Select(h => h.Id));
    }

    /// <summary>
    /// Filters compose rather than replace one another, so a department and a date
    /// range together narrow to the intersection.
    /// </summary>
    [Fact]
    public async Task Filters_compose()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var result = await AdminQuery(db, q =>
        {
            q.DepartmentId = 2;
            q.From = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        });

        Assert.Equal(["h-out-2"], result.Items.Select(h => h.Id));
    }

    /// <summary>
    /// Total has to count the whole filtered set, not the page — it is what the
    /// X-Total-Count header carries, and a page-sized total would make the header
    /// useless for paging through.
    /// </summary>
    [Fact]
    public async Task Paging_returns_one_page_and_the_unpaged_total()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var unpaged = await AdminQuery(db, _ => { });
        var firstPage = await AdminQuery(db, q => { q.Page = 1; q.PageSize = 2; });
        var secondPage = await AdminQuery(db, q => { q.Page = 2; q.PageSize = 2; });

        Assert.Equal(3, unpaged.Items.Count);
        Assert.Null(unpaged.Page);
        Assert.Equal(3, unpaged.Total);

        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal(3, firstPage.Total);

        Assert.Single(secondPage.Items);
        Assert.Equal(3, secondPage.Total);

        // The two pages together cover the set exactly once.
        Assert.Equal(
            unpaged.Items.Select(h => h.Id).OrderBy(id => id, StringComparer.Ordinal),
            firstPage.Items.Concat(secondPage.Items).Select(h => h.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// Paging must not be able to reach past the scope filter — a manager paging
    /// through sees only their own scope, however large a page they ask for.
    /// </summary>
    [Fact]
    public async Task Paging_does_not_escape_the_scope_filter()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(
            new GetTimesheetStatusHistoryList.Query
            {
                RequestingUserId = ManagerUserId,
                IsManager = true,
                Page = 1,
                PageSize = 200,
            },
            CancellationToken.None);

        Assert.Equal(["ts-in"], result.Items.Select(h => h.TimesheetId));
        Assert.Equal(1, result.Total);
    }

    private static ServiceProvider BuildProvider(AppDbContext db) =>
        new ServiceCollection()
            .AddSingleton(db)
            // MediatR 13's license accessor resolves ILoggerFactory during construction.
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetTimesheetStatusHistoryList>())
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

        // This read action does not touch the notifications hub; a null here fails
        // loudly if that ever stops being true.
        return new TimesheetsController(provider.GetRequiredService<AppDbContext>(), null!)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static async Task<List<string>> EndpointHistoryFor(
        IServiceProvider provider,
        string userId,
        string employeeProfileId,
        params string[] roles)
    {
        var response = await ControllerFor(provider, userId, roles)
            .GetEmployeeStatusHistories(employeeProfileId, page: null, pageSize: null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var items = Assert.IsAssignableFrom<IEnumerable<TimesheetStatusHistoryDto>>(ok.Value);
        return items.Select(h => h.TimesheetId).ToList();
    }

    /// <summary>
    /// The behaviour the 403 used to make impossible.
    /// </summary>
    [Fact]
    public async Task The_endpoint_serves_an_employee_their_own_trail()
    {
        using var db = TestDb.Create();
        SeedWorld(db);
        using var provider = BuildProvider(db);

        var timesheetIds = await EndpointHistoryFor(provider, "in-u", "in-p", AppRoles.Employee);

        Assert.Equal(["ts-in"], timesheetIds);
    }

    /// <summary>
    /// An out-of-scope request is answered with an empty list, matching what
    /// {id}/history does rather than the blanket refusal this replaced.
    /// </summary>
    [Fact]
    public async Task The_endpoint_answers_an_out_of_scope_request_with_an_empty_list()
    {
        using var db = TestDb.Create();
        SeedWorld(db);
        using var provider = BuildProvider(db);

        var timesheetIds = await EndpointHistoryFor(provider, "in-u", "out-p", AppRoles.Employee);

        Assert.Empty(timesheetIds);
    }

    [Fact]
    public async Task The_endpoint_scopes_a_manager_and_serves_an_admin_anybody()
    {
        using var db = TestDb.Create();
        SeedWorld(db);
        using var provider = BuildProvider(db);

        Assert.Equal(
            ["ts-in"],
            await EndpointHistoryFor(provider, ManagerUserId, "in-p", AppRoles.Manager));
        Assert.Empty(
            await EndpointHistoryFor(provider, ManagerUserId, "out-p", AppRoles.Manager));
        Assert.Equal(
            ["ts-out"],
            (await EndpointHistoryFor(provider, "admin-u", "out-p", AppRoles.Admin)).Distinct());
    }
}
