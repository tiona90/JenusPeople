using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Nobody decides their own leave. A Manager's own request sits inside their own
/// department scope, and an HR Administrator's own request is department-less and
/// so inside every HR Administrator's reach — scope alone let either approve
/// themselves. Timesheets already refuse this (OwnTimesheetMessage); this is the
/// leave side, on both status paths.
/// </summary>
public class LeaveSelfDecisionTests
{
    private const int Dept = 1;
    private const int ManagerOnlyType = 1;
    private const int HrOnlyType = 2;

    private const string Manager = "u-mgr";
    private const string OtherManager = "u-mgr2";
    private const string Hr = "u-hr";
    private const string OtherHr = "u-hr2";
    private const string Colleague = "u-col";

    private static readonly DateTime Start = new(2027, 3, 1); // Monday
    private static readonly DateTime End = new(2027, 3, 5);   // Friday

    private static async Task<AppDbContext> WorldAsync()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });
        db.Departments.Add(new Department { Id = Dept, Name = "Engineering", Code = "ENG" });
        db.Roles.AddRange(
            new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() },
            new Role { Id = "r-mgr", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() });
        db.Users.AddRange(
            new User { Id = Manager, UserName = Manager, Email = "mgr@t.local", DisplayName = "Nikos Manager" },
            new User { Id = OtherManager, UserName = OtherManager, Email = "mgr2@t.local", DisplayName = "Eleni Manager" },
            new User { Id = Hr, UserName = Hr, Email = "hr@t.local", DisplayName = "Helen HR" },
            new User { Id = OtherHr, UserName = OtherHr, Email = "hr2@t.local", DisplayName = "Petros HR" },
            new User { Id = Colleague, UserName = Colleague, Email = "col@t.local", DisplayName = "Andreas Georgiou" });
        db.UserRoles.AddRange(
            new UserRole { UserId = Manager, RoleId = "r-mgr" },
            new UserRole { UserId = OtherManager, RoleId = "r-mgr" },
            new UserRole { UserId = Hr, RoleId = "r-hr" },
            new UserRole { UserId = OtherHr, RoleId = "r-hr" });
        db.EmployeeProfiles.AddRange(
            new EmployeeProfile { Id = "p-mgr", UserId = Manager, DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 20 },
            new EmployeeProfile { Id = "p-mgr2", UserId = OtherManager, DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 20 },
            new EmployeeProfile { Id = "p-col", UserId = Colleague, DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 20 },
            new EmployeeProfile { Id = "p-hr", UserId = Hr, DepartmentId = null, AnnualLeaveEntitlement = 20, LeaveBalance = 20 },
            new EmployeeProfile { Id = "p-hr2", UserId = OtherHr, DepartmentId = null, AnnualLeaveEntitlement = 20, LeaveBalance = 20 });
        db.LeaveTypes.AddRange(
            new LeaveType { Id = ManagerOnlyType, Name = "Annual Leave", IsActive = true, AffectsBalance = true, DefaultAllowance = 20, RequiresManagerApproval = true },
            new LeaveType { Id = HrOnlyType, Name = "Sabbatical", IsActive = true, AffectsBalance = false, RequiresHrApproval = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task SeedManagersLeaveAsync(AppDbContext db)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L-mgr", EmployeeId = Manager, EmployeeProfileId = "p-mgr", DepartmentId = Dept,
            LeaveTypeId = ManagerOnlyType, StartDate = Start, EndDate = End, Reason = "Trip",
            DelegateId = Colleague, Status = AnnualLeaveStatus.Pending, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task SeedHrsLeaveAsync(AppDbContext db)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L-hr", EmployeeId = Hr, EmployeeProfileId = "p-hr", DepartmentId = null,
            LeaveTypeId = HrOnlyType, StartDate = Start, EndDate = End, Reason = "Trip",
            Status = AnnualLeaveStatus.AwaitingHrApproval, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static Task<Result<Unit>> DecideAsync(AppDbContext db, string leaveId, string byUserId, bool asHr, AnnualLeaveStatus status) =>
        new UpdateLeaveStatus.Handler(db, new FakeEmailService()).Handle(new UpdateLeaveStatus.Command
        {
            LeaveId = leaveId,
            ChangedByUserId = byUserId,
            IsAdmin = asHr,
            IsManager = !asHr,
            Request = new UpdateLeaveStatusRequest { Status = status, StatusComment = "Reason" },
        }, CancellationToken.None);

    private static Task<Result<Unit>> EditOwnManagerLeaveAsync(AppDbContext db, AnnualLeaveStatus? status) =>
        new EditAnnualLeave.Handler(db, new FakeEmailService()).Handle(new EditAnnualLeave.Command
        {
            ChangedByUserId = Manager, IsAdmin = false, IsManager = true,
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L-mgr", LeaveTypeId = ManagerOnlyType, StartDate = Start, EndDate = End,
                Reason = "Rebooked", DelegateId = Colleague, Status = status,
            },
        }, CancellationToken.None);

    private static async Task<AnnualLeaveStatus> StatusAsync(AppDbContext db, string id) =>
        (await db.AnnualLeaves.AsNoTracking().FirstAsync(l => l.Id == id)).Status;

    [Theory]
    [InlineData(AnnualLeaveStatus.Approved)]
    [InlineData(AnnualLeaveStatus.Rejected)]
    public async Task A_manager_cannot_decide_their_own_leave(AnnualLeaveStatus decision)
    {
        using var db = await WorldAsync();
        await SeedManagersLeaveAsync(db);

        var result = await DecideAsync(db, "L-mgr", Manager, asHr: false, decision);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateLeaveStatus.OwnLeaveMessage, result.Error);
        Assert.Equal(AnnualLeaveStatus.Pending, await StatusAsync(db, "L-mgr"));
    }

    [Fact]
    public async Task Another_manager_in_the_department_still_decides_it()
    {
        using var db = await WorldAsync();
        await SeedManagersLeaveAsync(db);

        var result = await DecideAsync(db, "L-mgr", OtherManager, asHr: false, AnnualLeaveStatus.Approved);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Approved, await StatusAsync(db, "L-mgr"));
    }

    [Fact]
    public async Task An_hr_administrator_cannot_approve_their_own_department_less_leave()
    {
        using var db = await WorldAsync();
        await SeedHrsLeaveAsync(db);

        var result = await DecideAsync(db, "L-hr", Hr, asHr: true, AnnualLeaveStatus.Approved);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateLeaveStatus.OwnLeaveMessage, result.Error);
        Assert.Equal(AnnualLeaveStatus.AwaitingHrApproval, await StatusAsync(db, "L-hr"));
    }

    [Fact]
    public async Task Another_hr_administrator_still_approves_it()
    {
        using var db = await WorldAsync();
        await SeedHrsLeaveAsync(db);

        var result = await DecideAsync(db, "L-hr", OtherHr, asHr: true, AnnualLeaveStatus.Approved);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AnnualLeaveStatus.Approved, await StatusAsync(db, "L-hr"));
    }

    [Fact]
    public async Task A_manager_cannot_approve_their_own_leave_from_the_edit_dialog()
    {
        using var db = await WorldAsync();
        await SeedManagersLeaveAsync(db);

        var result = await EditOwnManagerLeaveAsync(db, AnnualLeaveStatus.Approved);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateLeaveStatus.OwnLeaveMessage, result.Error);
        Assert.Equal(AnnualLeaveStatus.Pending, await StatusAsync(db, "L-mgr"));
    }

    [Fact]
    public async Task A_manager_can_still_edit_their_own_pending_leave_without_a_status_change()
    {
        using var db = await WorldAsync();
        await SeedManagersLeaveAsync(db);

        var result = await EditOwnManagerLeaveAsync(db, status: null);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await db.AnnualLeaves.AsNoTracking().FirstAsync(l => l.Id == "L-mgr");
        Assert.Equal("Rebooked", stored.Reason);
        Assert.Equal(AnnualLeaveStatus.Pending, stored.Status);
    }
}
