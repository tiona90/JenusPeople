using Application.Core;
using Application.LeaveTypes.Commands;
using Application.LeaveTypes.DTOs;
using AutoMapper;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Turning RequiresApproval off on an active leave type auto-approves everything
/// already pending against it. Each of those approvals has to clear the
/// employee's balance, and this handler used to check that through the throwing
/// wrapper — so an employee without the days turned an ordinary settings change
/// into a 500, with the Result&lt;T&gt; the handler declares never reached.
///
/// It now maps the same message to Result.Conflict, as the three other handlers
/// that approve leave already do.
/// </summary>
public class LeaveTypeAutoApprovalBalanceTests
{
    private const int LeaveTypeId = 1;

    private static IMapper BuildMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfiles>(),
            NullLoggerFactory.Instance).CreateMapper();

    /// <summary>
    /// A leave type that currently requires approval and counts against balance,
    /// which is what puts the auto-approval branch in play when RequiresApproval
    /// is switched off.
    /// </summary>
    private static void SeedLeaveType(AppDbContext db) =>
        db.LeaveTypes.Add(new LeaveType
        {
            Id = LeaveTypeId,
            Name = "Annual",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = true,
        });

    /// <summary>
    /// One employee with an entitlement, and one pending five-business-day request
    /// waiting on approval. EmployeeId is a user id; the profile is found through
    /// EmployeeProfileId.
    /// </summary>
    private static void SeedPendingRequest(AppDbContext db, string userId, int entitlement)
    {
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = $"p-{userId}",
            UserId = userId,
            AnnualLeaveEntitlement = entitlement,
        });

        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = $"L-{userId}",
            EmployeeId = userId,
            EmployeeProfileId = $"p-{userId}",
            LeaveTypeId = LeaveTypeId,
            Status = AnnualLeaveStatus.Pending,
            Reason = "Booked already",
            // Mon-Fri: five business days, no weekend to discount.
            StartDate = new DateTime(2024, 3, 4),
            EndDate = new DateTime(2024, 3, 8),
        });
    }

    /// <summary>The settings change that triggers the auto-approval sweep.</summary>
    private static UpdateLeaveType.Command StopRequiringApproval() => new()
    {
        Id = LeaveTypeId,
        LeaveType = new UpsertLeaveTypeRequest
        {
            Name = "Annual",
            RequiresApproval = false,
            IsActive = true,
            AffectsBalance = true,
        },
    };

    private static Task<Result<LeaveTypeDto>> Handle(AppDbContext db, UpdateLeaveType.Command command) =>
        new UpdateLeaveType.Handler(db, BuildMapper()).Handle(command, CancellationToken.None);

    private static Task<AnnualLeaveStatus> StatusOf(AppDbContext db, string userId) =>
        db.AnnualLeaves.AsNoTracking()
            .Where(l => l.Id == $"L-{userId}")
            .Select(l => l.Status)
            .SingleAsync();

    [Fact]
    public async Task Insufficient_balance_is_returned_as_a_conflict_rather_than_thrown()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db);
        // One day of entitlement against a five-day request.
        SeedPendingRequest(db, "u-short", entitlement: 1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, StopRequiringApproval());

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("Insufficient leave balance", result.Error);
    }

    /// <summary>
    /// The refusal comes before SaveChanges, so the sweep leaves no trace: the
    /// request the admin was told about is still waiting, not silently approved.
    /// </summary>
    [Fact]
    public async Task A_refused_sweep_leaves_the_pending_request_pending()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db);
        SeedPendingRequest(db, "u-short", entitlement: 1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await Handle(db, StopRequiringApproval());

        Assert.Equal(AnnualLeaveStatus.Pending, await StatusOf(db, "u-short"));
        Assert.False(await db.LeaveStatusHistories.AsNoTracking().AnyAsync());
    }

    /// <summary>
    /// One employee short of balance stops the whole sweep, so a colleague who
    /// had the days is not left approved by a call that reported failure.
    /// </summary>
    [Fact]
    public async Task One_employee_short_of_balance_does_not_half_approve_the_rest()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db);
        SeedPendingRequest(db, "u-ample", entitlement: 25);
        SeedPendingRequest(db, "u-short", entitlement: 1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, StopRequiringApproval());

        Assert.False(result.IsSuccess);
        Assert.Equal(AnnualLeaveStatus.Pending, await StatusOf(db, "u-ample"));
        Assert.Equal(AnnualLeaveStatus.Pending, await StatusOf(db, "u-short"));
    }

    [Fact]
    public async Task Sufficient_balance_auto_approves_the_pending_request()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db);
        SeedPendingRequest(db, "u-ample", entitlement: 25);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, StopRequiringApproval());

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.RequiresApproval);
        Assert.Equal(AnnualLeaveStatus.Approved, await StatusOf(db, "u-ample"));
    }
}
