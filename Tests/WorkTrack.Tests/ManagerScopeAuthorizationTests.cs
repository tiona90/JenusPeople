using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using Application.Timesheets.Commands;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// (4) Manager authorization scoping. A Manager may act only on resources in
/// their assigned department(s) or on their direct reports — and must be denied
/// everything outside that scope. Covered at the resolver level and through the
/// approve/reject handlers for both timesheets and leave.
/// </summary>
public class ManagerScopeAuthorizationTests
{
    // Manager "mgr" owns profile "mp" in department 1.
    private const string ManagerUserId = "mgr";
    private const string ManagerProfileId = "mp";

    private static void SeedManager(AppDbContext db)
    {
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ManagerProfileId,
            UserId = ManagerUserId,
            DepartmentId = 1,
        });
    }

    // ── Resolver scoping ────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolver_scopes_to_own_department_and_direct_reports_only()
    {
        using var db = TestDb.Create();
        SeedManager(db);
        // Direct report in dept 1.
        db.EmployeeProfiles.Add(new EmployeeProfile
        { Id = "rp", UserId = "report", DepartmentId = 1, ManagerId = ManagerProfileId });
        // Unrelated employee in dept 2, different manager.
        db.EmployeeProfiles.Add(new EmployeeProfile
        { Id = "op", UserId = "outsider", DepartmentId = 2, ManagerId = null });
        await db.SaveChangesAsync();

        var scope = await ManagerAccessScopeResolver.ResolveAsync(db, ManagerUserId, CancellationToken.None);

        Assert.Contains(1, scope.ManagedDepartmentIds);
        Assert.DoesNotContain(2, scope.ManagedDepartmentIds);
        Assert.Contains("report", scope.DirectReportUserIds);
        Assert.DoesNotContain("outsider", scope.DirectReportUserIds);
        Assert.Contains(ManagerProfileId, scope.ManagerProfileIds);
    }

    // ── Timesheet approval scoping ───────────────────────────────────────────────

    private static Timesheet SeedTimesheet(AppDbContext db, int departmentId, string employeeProfileId)
    {
        var ts = new Timesheet
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeProfileId = employeeProfileId,
            DepartmentId = departmentId,
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 7),
            TotalHours = 40m,
            Status = TimesheetStatus.Submitted,
        };
        db.Timesheets.Add(ts);
        db.SaveChanges();
        return ts;
    }

    private static UpdateTimesheetStatus.Handler TimesheetHandler(AppDbContext db) =>
        new(db, new FakeEmailService(), NullLogger<UpdateTimesheetStatus.Handler>.Instance);

    [Fact]
    public async Task Manager_cannot_approve_a_timesheet_outside_their_department()
    {
        using var db = TestDb.Create();
        SeedManager(db);
        // Timesheet in dept 2, employee not reporting to the manager.
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "e2", UserId = "u2", DepartmentId = 2, ManagerId = null });
        var ts = SeedTimesheet(db, departmentId: 2, employeeProfileId: "e2");

        var result = await TimesheetHandler(db).Handle(
            new UpdateTimesheetStatus.Command
            {
                Id = ts.Id,
                NewStatus = TimesheetStatus.Approved,
                RequestingUserId = ManagerUserId,
                IsAdmin = false,
                IsManager = true,
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var reloaded = await db.Timesheets.FindAsync(ts.Id);
        Assert.Equal(TimesheetStatus.Submitted, reloaded!.Status); // not approved
    }

    [Fact]
    public async Task Manager_can_approve_a_timesheet_in_their_department()
    {
        using var db = TestDb.Create();
        SeedManager(db);
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "e1", UserId = "u1", DepartmentId = 1, ManagerId = ManagerProfileId });
        var ts = SeedTimesheet(db, departmentId: 1, employeeProfileId: "e1");

        var result = await TimesheetHandler(db).Handle(
            new UpdateTimesheetStatus.Command
            {
                Id = ts.Id,
                NewStatus = TimesheetStatus.Approved,
                RequestingUserId = ManagerUserId,
                IsAdmin = false,
                IsManager = true,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var reloaded = await db.Timesheets.FindAsync(ts.Id);
        Assert.Equal(TimesheetStatus.Approved, reloaded!.Status);
    }

    // ── Leave status scoping ─────────────────────────────────────────────────────

    [Fact]
    public async Task Manager_cannot_change_leave_status_outside_their_scope()
    {
        using var db = TestDb.Create();
        SeedManager(db);
        // Leave belongs to dept 2, employee is not a direct report.
        var leave = new AnnualLeave
        {
            Id = "leave-1",
            EmployeeId = "outsider",
            DepartmentId = 2,
            Status = AnnualLeaveStatus.Pending,
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 3),
        };
        db.AnnualLeaves.Add(leave);
        await db.SaveChangesAsync();

        var handler = new UpdateLeaveStatus.Handler(db, new FakeEmailService(), new FakeChatNotificationService());
        var result = await handler.Handle(
            new UpdateLeaveStatus.Command
            {
                LeaveId = leave.Id,
                Request = new UpdateLeaveStatusRequest { Status = AnnualLeaveStatus.Approved },
                ChangedByUserId = ManagerUserId,
                IsAdmin = false,
                IsManager = true,
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        var reloaded = await db.AnnualLeaves.FindAsync(leave.Id);
        Assert.Equal(AnnualLeaveStatus.Pending, reloaded!.Status); // unchanged
    }
}
