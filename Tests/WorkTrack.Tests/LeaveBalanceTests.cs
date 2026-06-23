using Application.AnnualLeaves.Commands;
using Domain;
using Domain.Services;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// (1) Leave-balance computation, including AffectsBalance handling.
/// The pure arithmetic lives in <see cref="LeaveCalculationService"/>; the
/// AffectsBalance gate + prior-usage aggregation live in the (internal)
/// <see cref="AnnualLeaveBalanceCalculator"/>, exercised here over an in-memory DB.
/// </summary>
public class LeaveBalanceTests
{
    // ── Pure arithmetic ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(20, 5, 15)]
    [InlineData(20, 20, 0)]
    [InlineData(20, 25, 0)]   // floored at zero (e.g. mid-year hire)
    public void RemainingBalance_is_floored_at_zero(int entitlement, int used, int expected)
    {
        Assert.Equal(expected, LeaveCalculationService.CalculateRemainingBalance(entitlement, used));
    }

    [Fact]
    public void BusinessDaysInLeaveYear_clips_to_the_leave_year_window()
    {
        // Leave 2023-12-28 (Thu) .. 2024-01-03 (Wed), counted into leave-year 2024
        // (Jan-start). Only 2024-01-01..03 (Mon,Tue,Wed) falls inside → 3 days.
        var days = LeaveCalculationService.CalculateBusinessDaysInLeaveYear(
            new DateTime(2023, 12, 28), new DateTime(2024, 1, 3),
            leaveYearKey: 2024, startMonth: 1);
        Assert.Equal(3, days);
    }

    // ── AffectsBalance handling + sufficiency ───────────────────────────────────

    private static LeaveType SeedLeaveType(AppDbContext db, int id, bool affectsBalance)
    {
        var lt = new LeaveType { Id = id, Name = $"Type{id}", IsActive = true, AffectsBalance = affectsBalance };
        db.LeaveTypes.Add(lt);
        return lt;
    }

    private static AnnualLeave Candidate(string employeeId, int leaveTypeId, string start, string end) => new()
    {
        Id = Guid.NewGuid().ToString(),
        EmployeeId = employeeId,
        LeaveTypeId = leaveTypeId,
        StartDate = DateTime.Parse(start),
        EndDate = DateTime.Parse(end),
    };

    private static Task<string?> Check(AppDbContext db, int entitlement, AnnualLeave candidate)
    {
        var profile = new EmployeeProfile { UserId = candidate.EmployeeId, AnnualLeaveEntitlement = entitlement };
        return AnnualLeaveBalanceCalculator.CheckSufficientBalanceAsync(
            db, profile, candidate, excludeLeaveId: candidate.Id, CancellationToken.None);
    }

    [Fact]
    public async Task NonBalance_leave_type_is_never_blocked_even_when_over_entitlement()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db, 1, affectsBalance: false);
        await db.SaveChangesAsync();

        // Entitlement 1 but a 5-day request: would exceed — yet AffectsBalance=false skips the check.
        var error = await Check(db, entitlement: 1, Candidate("u1", 1, "2024-01-01", "2024-01-05"));

        Assert.Null(error);
    }

    [Fact]
    public async Task Unconfigured_entitlement_of_zero_skips_the_check()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db, 1, affectsBalance: true);
        await db.SaveChangesAsync();

        var error = await Check(db, entitlement: 0, Candidate("u1", 1, "2024-01-01", "2024-01-05"));

        Assert.Null(error);
    }

    [Fact]
    public async Task Sufficient_balance_returns_no_error()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db, 1, affectsBalance: true);
        await db.SaveChangesAsync();

        var error = await Check(db, entitlement: 20, Candidate("u1", 1, "2024-01-01", "2024-01-05"));

        Assert.Null(error);
    }

    [Fact]
    public async Task Insufficient_balance_returns_a_clear_error()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db, 1, affectsBalance: true);
        await db.SaveChangesAsync();

        // Entitlement 3, request is 5 business days → over by 2.
        var error = await Check(db, entitlement: 3, Candidate("u1", 1, "2024-01-01", "2024-01-05"));

        Assert.NotNull(error);
        Assert.Contains("Insufficient leave balance", error);
    }

    [Fact]
    public async Task Prior_approved_balance_leave_is_counted_against_the_new_request()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db, 1, affectsBalance: true);
        // Prior APPROVED leave: 2024-01-01..2024-01-10 = 8 business days already used.
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "prior",
            EmployeeId = "u1",
            LeaveTypeId = 1,
            Status = AnnualLeaveStatus.Approved,
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 10),
        });
        await db.SaveChangesAsync();

        // Entitlement 10, used 8 → remaining 2. New 5-day request (Feb) must fail.
        var error = await Check(db, entitlement: 10, Candidate("u1", 1, "2024-02-05", "2024-02-09"));

        Assert.NotNull(error);
        Assert.Contains("Insufficient leave balance", error);
    }

    [Fact]
    public async Task Prior_pending_leave_does_not_consume_balance()
    {
        using var db = TestDb.Create();
        SeedLeaveType(db, 1, affectsBalance: true);
        // PENDING (not Approved) prior leave — must not count.
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "prior",
            EmployeeId = "u1",
            LeaveTypeId = 1,
            Status = AnnualLeaveStatus.Pending,
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 1, 10),
        });
        await db.SaveChangesAsync();

        var error = await Check(db, entitlement: 10, Candidate("u1", 1, "2024-02-05", "2024-02-09"));

        Assert.Null(error);
    }
}
