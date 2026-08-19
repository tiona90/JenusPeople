using System.Security.Claims;
using API.Controllers;
using Application.Attendance.DTOs;
using Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// "On leave today" was always empty on both the team board and the company
/// dashboard. Both endpoints build their id list from EmployeeProfile.Id, then
/// filtered AnnualLeaves on EmployeeId — which is an AspNetUsers.Id. The two id
/// spaces never intersect, so the Approved-leave-spanning-today query matched
/// nothing and everyone on leave was reported as simply not checked in.
///
/// The queries now filter on AnnualLeave.EmployeeProfileId. These tests seed a
/// leave row the way CreateAnnualLeave writes one — EmployeeId holding the user
/// id and EmployeeProfileId holding the profile id — so a regression back to the
/// user-id column shows up as a zero count rather than as a passing test.
/// </summary>
public class AttendanceOnLeaveTodayTests
{
    private const string AdminUserId = "admin-u";
    private const string EmployeeUserId = "employee-u";
    private const string AdminProfileId = "admin-p";
    private const string EmployeeProfileId = "employee-p";
    private const int DepartmentId = 1;
    private const string DepartmentName = "Engineering";

    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = DepartmentId, Name = DepartmentName, Code = "ENG" });

        db.Users.Add(new User { Id = AdminUserId, UserName = "admin", DisplayName = "Ada Admin" });
        db.Users.Add(new User { Id = EmployeeUserId, UserName = "employee", DisplayName = "Eve Employee" });

        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = AdminProfileId,
            UserId = AdminUserId,
            DepartmentId = DepartmentId,
        });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = EmployeeProfileId,
            UserId = EmployeeUserId,
            DepartmentId = DepartmentId,
        });

        db.SaveChanges();
        return db;
    }

    /// <summary>
    /// Mirrors what CreateAnnualLeave persists: the user id in EmployeeId and the
    /// profile id in EmployeeProfileId. Setting only one of them would let a query
    /// against the wrong column pass by accident.
    /// </summary>
    private static void AddLeave(
        AppDbContext db,
        string userId,
        string? profileId,
        AnnualLeaveStatus status = AnnualLeaveStatus.Approved,
        int startOffsetDays = -1,
        int endOffsetDays = 1)
    {
        var now = DateTime.UtcNow;
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = userId,
            EmployeeProfileId = profileId,
            DepartmentId = DepartmentId,
            Status = status,
            StartDate = now.AddDays(startOffsetDays),
            EndDate = now.AddDays(endOffsetDays),
        });
        db.SaveChanges();
    }

    private static AttendanceController ControllerFor(AppDbContext db, string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        };

        return new AttendanceController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    /// <summary>
    /// Both actions hand back Ok(...), so the payload arrives as the ObjectResult in
    /// ActionResult&lt;T&gt;.Result and Value stays null.
    /// </summary>
    private static T Payload<T>(ActionResult<T> action) => action.Result switch
    {
        ObjectResult objectResult => (T)objectResult.Value!,
        _ => action.Value!,
    };

    private static async Task<CompanyAttendanceDto> GetCompanyAsync(AppDbContext db) =>
        Payload(await ControllerFor(db, AdminUserId, AppRoles.Admin).GetCompany());

    private static async Task<TeamAttendanceDto> GetTeamAsync(AppDbContext db) =>
        Payload(await ControllerFor(db, AdminUserId, AppRoles.Admin).GetTeam());

    [Fact]
    public async Task Company_dashboard_counts_an_employee_on_approved_leave_today()
    {
        using var db = SeedWorld();
        AddLeave(db, EmployeeUserId, EmployeeProfileId);

        var company = await GetCompanyAsync(db);

        Assert.Equal(2, company.Total);
        Assert.Equal(1, company.Leave);

        var department = Assert.Single(company.Departments, d => d.Name == DepartmentName);
        Assert.Equal(1, department.Leave);
    }

    [Fact]
    public async Task Team_board_shows_an_employee_on_approved_leave_today_as_on_leave()
    {
        using var db = SeedWorld();
        AddLeave(db, EmployeeUserId, EmployeeProfileId);

        var team = await GetTeamAsync(db);

        var member = Assert.Single(team.Members, m => m.EmployeeId == EmployeeProfileId);
        Assert.Equal("leave", member.Status);
        Assert.Equal("On leave today", member.TodayNote);
    }

    /// <summary>
    /// The colleague who is not away has to stay "out", or a filter matching
    /// everything would satisfy the two tests above.
    /// </summary>
    [Fact]
    public async Task A_colleague_who_is_not_away_is_not_marked_on_leave()
    {
        using var db = SeedWorld();
        AddLeave(db, EmployeeUserId, EmployeeProfileId);

        var team = await GetTeamAsync(db);

        var admin = Assert.Single(team.Members, m => m.EmployeeId == AdminProfileId);
        Assert.Equal("out", admin.Status);
    }

    /// <summary>
    /// Rows the demo seeder wrote before it started setting EmployeeProfileId have
    /// a null there. They must not match anybody rather than matching everybody.
    /// </summary>
    [Fact]
    public async Task A_leave_row_with_no_profile_id_counts_for_nobody()
    {
        using var db = SeedWorld();
        AddLeave(db, EmployeeUserId, profileId: null);

        var company = await GetCompanyAsync(db);
        var team = await GetTeamAsync(db);

        Assert.Equal(0, company.Leave);
        Assert.All(team.Members, m => Assert.NotEqual("leave", m.Status));
    }

    [Fact]
    public async Task Leave_that_does_not_span_today_is_not_counted()
    {
        using var db = SeedWorld();
        AddLeave(db, EmployeeUserId, EmployeeProfileId, startOffsetDays: 30, endOffsetDays: 35);

        var company = await GetCompanyAsync(db);

        Assert.Equal(0, company.Leave);
    }

    [Fact]
    public async Task Leave_still_awaiting_approval_is_not_counted()
    {
        using var db = SeedWorld();
        AddLeave(db, EmployeeUserId, EmployeeProfileId, status: AnnualLeaveStatus.Pending);

        var company = await GetCompanyAsync(db);

        Assert.Equal(0, company.Leave);
    }

    /// <summary>
    /// A leave row whose profile id belongs to somebody else must not spill onto
    /// the employee whose user id it carries — the mirror image of the original
    /// bug, where the columns were crossed the other way.
    /// </summary>
    [Fact]
    public async Task Leave_is_attributed_to_the_profile_it_names_not_the_user_id()
    {
        using var db = SeedWorld();
        AddLeave(db, EmployeeUserId, AdminProfileId);

        var team = await GetTeamAsync(db);

        var employee = Assert.Single(team.Members, m => m.EmployeeId == EmployeeProfileId);
        var admin = Assert.Single(team.Members, m => m.EmployeeId == AdminProfileId);

        Assert.Equal("out", employee.Status);
        Assert.Equal("leave", admin.Status);
    }
}
