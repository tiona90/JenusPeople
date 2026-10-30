using Application.AnnualLeaves.Commands;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Deleting a leave is withdrawing a request nobody has decided yet. A decided one
/// — approved above all — is taken back through its status (UpdateLeaveStatus →
/// Cancelled), which applies CancellationRule, writes history and tells the
/// delegate and the managers. The Manager and HR branches of DeleteAnnualLeave used
/// to delete in any status, so a Manager could erase their own leave after taking
/// it and have the days handed back to their balance.
/// </summary>
public class LeaveDeletionTests
{
    private const int Dept = 1;
    private const string Manager = "u-mgr";
    private const string Hr = "u-hr";
    private const string Employee = "u-emp";

    private static async Task<AppDbContext> WorldAsync()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });
        db.Departments.Add(new Department { Id = Dept, Name = "Engineering", Code = "ENG" });
        db.EmployeeProfiles.AddRange(
            new EmployeeProfile { Id = "p-mgr", UserId = Manager, DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 15 },
            new EmployeeProfile { Id = "p-emp", UserId = Employee, DepartmentId = Dept, AnnualLeaveEntitlement = 20, LeaveBalance = 15 },
            new EmployeeProfile { Id = "p-hr", UserId = Hr, DepartmentId = null });
        db.UserDepartments.Add(new UserDepartment { UserId = Hr, DepartmentId = Dept });
        db.LeaveTypes.Add(new LeaveType { Id = 1, Name = "Annual Leave", IsActive = true, AffectsBalance = true, DefaultAllowance = 20, RequiresManagerApproval = true });
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task SeedAsync(AppDbContext db, string id, string employeeId, string profileId, AnnualLeaveStatus status)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = id, EmployeeId = employeeId, EmployeeProfileId = profileId, DepartmentId = Dept, LeaveTypeId = 1,
            StartDate = new DateTime(2026, 9, 7), EndDate = new DateTime(2026, 9, 11),
            Reason = "Trip", Status = status, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static Task<Result<Unit>> DeleteAsync(AppDbContext db, string id, string byUserId, bool isAdmin, bool isManager) =>
        new DeleteAnnualLeave.Handler(db).Handle(new DeleteAnnualLeave.Command
        {
            Id = id, RequestingUserId = byUserId, IsAdmin = isAdmin, IsManager = isManager,
        }, CancellationToken.None);

    [Theory]
    [InlineData(AnnualLeaveStatus.Approved)]
    [InlineData(AnnualLeaveStatus.Rejected)]
    [InlineData(AnnualLeaveStatus.Cancelled)]
    public async Task A_manager_cannot_delete_their_own_decided_leave(AnnualLeaveStatus status)
    {
        using var db = await WorldAsync();
        await SeedAsync(db, "L1", Manager, "p-mgr", status);

        var result = await DeleteAsync(db, "L1", Manager, isAdmin: false, isManager: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(DeleteAnnualLeave.DecidedRequestMessage, result.Error);
        Assert.True(await db.AnnualLeaves.AnyAsync(l => l.Id == "L1"));
    }

    [Fact]
    public async Task An_hr_administrator_cannot_delete_an_approved_leave_in_their_departments()
    {
        using var db = await WorldAsync();
        await SeedAsync(db, "L1", Employee, "p-emp", AnnualLeaveStatus.Approved);

        var result = await DeleteAsync(db, "L1", Hr, isAdmin: true, isManager: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(DeleteAnnualLeave.DecidedRequestMessage, result.Error);
        Assert.True(await db.AnnualLeaves.AnyAsync(l => l.Id == "L1"));
    }

    [Theory]
    [InlineData(AnnualLeaveStatus.Pending)]
    [InlineData(AnnualLeaveStatus.AwaitingHrApproval)]
    public async Task A_manager_still_withdraws_their_own_open_request(AnnualLeaveStatus status)
    {
        using var db = await WorldAsync();
        await SeedAsync(db, "L1", Manager, "p-mgr", status);

        var result = await DeleteAsync(db, "L1", Manager, isAdmin: false, isManager: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await db.AnnualLeaves.AnyAsync(l => l.Id == "L1"));
    }

    [Fact]
    public async Task An_hr_administrator_still_withdraws_an_open_request_in_their_departments()
    {
        using var db = await WorldAsync();
        await SeedAsync(db, "L1", Employee, "p-emp", AnnualLeaveStatus.Pending);

        var result = await DeleteAsync(db, "L1", Hr, isAdmin: true, isManager: false);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await db.AnnualLeaves.AnyAsync(l => l.Id == "L1"));
    }
}
