using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.AnnualLeaves.Validators;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// (2) Overlap / conflict detection between leave requests. The rule lives in the
/// FluentValidation validators: a new/edited request must not overlap an existing
/// Pending/Approved leave for the same employee. Overlap = StartDate &lt;= other.End
/// AND EndDate &gt;= other.Start. Rejected/Cancelled leaves are ignored; an edit
/// excludes the record being edited.
/// </summary>
public class LeaveOverlapTests
{
    private const string OverlapMessage =
        "This request overlaps with an existing pending or approved leave request.";

    private static void SeedLeave(AppDbContext db, string id, AnnualLeaveStatus status, string start, string end, string emp = "u1")
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = id,
            EmployeeId = emp,
            LeaveTypeId = 1,
            Status = status,
            StartDate = DateTime.Parse(start),
            EndDate = DateTime.Parse(end),
        });
    }

    private static CreateAnnualLeave.Command CreateCmd(string start, string end, string emp = "u1") => new()
    {
        AnnualLeave = new CreateAnnualLeaveRequest
        {
            EmployeeId = emp,
            LeaveTypeId = 1,
            StartDate = DateTime.Parse(start),
            EndDate = DateTime.Parse(end),
            Reason = "test",
        },
    };

    private static async Task<bool> HasOverlapError(CreateAnnualLeaveRequestValidator validator, CreateAnnualLeave.Command cmd)
    {
        var result = await validator.ValidateAsync(cmd);
        return result.Errors.Any(e => e.ErrorMessage == OverlapMessage);
    }

    [Fact]
    public async Task Create_overlapping_an_approved_leave_is_flagged()
    {
        using var db = TestDb.Create();
        SeedLeave(db, "a", AnnualLeaveStatus.Approved, "2024-01-01", "2024-01-05");
        await db.SaveChangesAsync();

        var validator = new CreateAnnualLeaveRequestValidator(db);
        // 2024-01-03..08 overlaps 2024-01-01..05.
        Assert.True(await HasOverlapError(validator, CreateCmd("2024-01-03", "2024-01-08")));
    }

    [Fact]
    public async Task Create_non_overlapping_leave_is_allowed()
    {
        using var db = TestDb.Create();
        SeedLeave(db, "a", AnnualLeaveStatus.Approved, "2024-01-01", "2024-01-05");
        await db.SaveChangesAsync();

        var validator = new CreateAnnualLeaveRequestValidator(db);
        // A clearly separate month — no overlap.
        Assert.False(await HasOverlapError(validator, CreateCmd("2024-02-01", "2024-02-05")));
    }

    [Fact]
    public async Task Create_touching_boundary_counts_as_overlap()
    {
        using var db = TestDb.Create();
        SeedLeave(db, "a", AnnualLeaveStatus.Approved, "2024-01-01", "2024-01-05");
        await db.SaveChangesAsync();

        var validator = new CreateAnnualLeaveRequestValidator(db);
        // New request starts the same day the existing one ends → inclusive overlap.
        Assert.True(await HasOverlapError(validator, CreateCmd("2024-01-05", "2024-01-08")));
    }

    [Fact]
    public async Task Create_overlap_ignores_rejected_and_cancelled_leaves()
    {
        using var db = TestDb.Create();
        SeedLeave(db, "r", AnnualLeaveStatus.Rejected, "2024-01-01", "2024-01-05");
        SeedLeave(db, "c", AnnualLeaveStatus.Cancelled, "2024-01-01", "2024-01-05");
        await db.SaveChangesAsync();

        var validator = new CreateAnnualLeaveRequestValidator(db);
        // Same dates as the rejected/cancelled leaves — those don't block.
        Assert.False(await HasOverlapError(validator, CreateCmd("2024-01-01", "2024-01-05")));
    }

    [Fact]
    public async Task Create_overlap_is_scoped_per_employee()
    {
        using var db = TestDb.Create();
        SeedLeave(db, "a", AnnualLeaveStatus.Approved, "2024-01-01", "2024-01-05", emp: "other-user");
        await db.SaveChangesAsync();

        var validator = new CreateAnnualLeaveRequestValidator(db);
        // Same dates but a different employee — not a conflict for u1.
        Assert.False(await HasOverlapError(validator, CreateCmd("2024-01-01", "2024-01-05", emp: "u1")));
    }

    [Fact]
    public async Task Edit_excludes_the_record_being_edited_from_its_own_overlap_check()
    {
        using var db = TestDb.Create();
        SeedLeave(db, "A", AnnualLeaveStatus.Approved, "2024-01-01", "2024-01-05");
        await db.SaveChangesAsync();

        var validator = new EditAnnualLeaveRequestValidator(db);
        var cmd = new EditAnnualLeave.Command
        {
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "A",
                LeaveTypeId = 1,
                StartDate = DateTime.Parse("2024-01-01"),
                EndDate = DateTime.Parse("2024-01-05"),
                Reason = "test",
            },
        };

        var result = await validator.ValidateAsync(cmd);
        Assert.DoesNotContain(result.Errors, e => e.ErrorMessage == OverlapMessage);
    }

    [Fact]
    public async Task Edit_overlapping_a_different_leave_is_flagged()
    {
        using var db = TestDb.Create();
        SeedLeave(db, "A", AnnualLeaveStatus.Approved, "2024-01-01", "2024-01-05");
        SeedLeave(db, "B", AnnualLeaveStatus.Approved, "2024-03-01", "2024-03-05");
        await db.SaveChangesAsync();

        var validator = new EditAnnualLeaveRequestValidator(db);
        // Move leave A onto leave B's window.
        var cmd = new EditAnnualLeave.Command
        {
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "A",
                LeaveTypeId = 1,
                StartDate = DateTime.Parse("2024-03-03"),
                EndDate = DateTime.Parse("2024-03-06"),
                Reason = "test",
            },
        };

        var result = await validator.ValidateAsync(cmd);
        Assert.Contains(result.Errors, e => e.ErrorMessage == OverlapMessage);
    }
}
