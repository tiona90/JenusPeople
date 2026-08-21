using System.Security.Claims;
using API.Controllers;
using Application.Attendance.DTOs;
using Application.Attendance.Queries;
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
/// The company dashboard counted every EmployeeProfile, so an Admin who happens
/// to hold one was reported as an employee: the hero read "12 employees" over a
/// population the Users panel and GetTeammateList both treat as 11, and the
/// admin's own name turned up in the "not checked in" feed every morning.
///
/// Attendance now applies the exclusion the rest of the employee-facing queries
/// already apply. These tests seed a real Admin UserRole row — the earlier
/// attendance tests only put the role in the caller's claims, which the query
/// cannot see — so a regression shows up as an inflated count.
/// </summary>
public class AttendanceExcludesAdminsTests
{
    private const string AdminUserId = "admin-u";
    private const string AdminProfileId = "admin-p";
    private const int DepartmentId = 1;
    private const string DepartmentName = "Engineering";

    /// <summary>
    /// One Admin and two employees, all three holding a profile in the same
    /// department. Only the two employees are the tracked workforce.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = DepartmentId, Name = DepartmentName, Code = "ENG" });

        var adminRole = new Role
        {
            Id = "r-admin",
            Name = AppRoles.Admin,
            NormalizedName = AppRoles.Admin.ToUpperInvariant(),
        };
        db.Roles.Add(adminRole);

        SeedProfile(db, AdminUserId, AdminProfileId, "Ada Admin");
        db.UserRoles.Add(new UserRole { UserId = AdminUserId, RoleId = adminRole.Id });

        SeedProfile(db, "employee-one-u", "employee-one-p", "Eve Employee");
        SeedProfile(db, "employee-two-u", "employee-two-p", "Bob Employee");

        db.SaveChanges();
        return db;
    }

    private static void SeedProfile(AppDbContext db, string userId, string profileId, string displayName)
    {
        db.Users.Add(new User { Id = userId, UserName = userId, DisplayName = displayName });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = profileId,
            UserId = userId,
            DepartmentId = DepartmentId,
        });
    }

    private static ServiceProvider BuildProvider(AppDbContext db) =>
        new ServiceCollection()
            .AddSingleton(db)
            // MediatR 13's license accessor resolves ILoggerFactory during construction.
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<GetCompanyAttendance>())
            .BuildServiceProvider();

    private static AttendanceController ControllerFor(IServiceProvider provider, string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
        };

        return new AttendanceController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static T Payload<T>(ActionResult action) =>
        (T)Assert.IsType<OkObjectResult>(action).Value!;

    [Fact]
    public async Task Company_totals_count_only_the_non_admin_workforce()
    {
        using var db = SeedWorld();
        using var provider = BuildProvider(db);

        var company = Payload<CompanyAttendanceDto>(
            await ControllerFor(provider, AdminUserId, AppRoles.Admin).GetCompany(CancellationToken.None));

        Assert.Equal(2, company.Total);

        // The per-department breakdown has to agree with the headline, or the
        // dashboard shows two different sizes for the same company again.
        var department = Assert.Single(company.Departments, d => d.Name == DepartmentName);
        Assert.Equal(2, department.Total);
        Assert.Equal(company.Total, company.Departments.Sum(d => d.Total));
    }

    [Fact]
    public async Task Company_activity_feed_never_names_an_admin()
    {
        using var db = SeedWorld();
        using var provider = BuildProvider(db);

        var company = Payload<CompanyAttendanceDto>(
            await ControllerFor(provider, AdminUserId, AppRoles.Admin).GetCompany(CancellationToken.None));

        Assert.DoesNotContain("Ada Admin", company.Recent.Select(r => r.EmployeeName));
    }

    [Fact]
    public async Task Team_board_excludes_admins()
    {
        using var db = SeedWorld();
        using var provider = BuildProvider(db);

        var team = Payload<TeamAttendanceDto>(
            await ControllerFor(provider, AdminUserId, AppRoles.Admin).GetTeam(CancellationToken.None));

        Assert.DoesNotContain(AdminProfileId, team.Members.Select(m => m.EmployeeId));
        Assert.Equal(2, team.Members.Count);
    }
}
