using Application.Timesheets.Commands;
using Application.Timesheets.Validators;
using Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// (5) Timesheet status transitions: Draft→Submitted, Rejected→Resubmitted
/// (SubmitTimesheet) and Submitted/Resubmitted→Approved/Rejected
/// (UpdateTimesheetStatus). Admin acts to keep the focus on the transition rules
/// rather than authorization. Employee/User rows are intentionally not seeded so
/// the post-save notification short-circuits.
/// </summary>
public class TimesheetStatusTransitionTests
{
    private static Timesheet SeedTimesheet(AppDbContext db, TimesheetStatus status)
    {
        // SubmitTimesheet does .Include(t => t.Employee).ThenInclude(e => e.User) over
        // required relationships, so the full User → EmployeeProfile → Timesheet chain must
        // exist or the timesheet is filtered out of the (inner-joined) query entirely.
        db.Users.Add(new User { Id = "emp-user-1", UserName = "emp", Email = "emp@test.local", DisplayName = "Emp" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "emp-profile-1", UserId = "emp-user-1", DepartmentId = 1 });
        var ts = new Timesheet
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeProfileId = "emp-profile-1",
            DepartmentId = 1,
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 7),
            TotalHours = 40m,
            Status = status,
        };
        db.Timesheets.Add(ts);
        db.SaveChanges();
        return ts;
    }

    private static SubmitTimesheet.Handler SubmitHandler(AppDbContext db) =>
        new(db, new FakeEmailService(), NullLogger<SubmitTimesheet.Handler>.Instance);

    private static UpdateTimesheetStatus.Handler StatusHandler(AppDbContext db) =>
        new(db, new FakeEmailService(), NullLogger<UpdateTimesheetStatus.Handler>.Instance);

    [Fact]
    public async Task Draft_is_submitted()
    {
        using var db = TestDb.Create();
        var ts = SeedTimesheet(db, TimesheetStatus.Draft);

        var result = await SubmitHandler(db).Handle(
            new SubmitTimesheet.Command { Id = ts.Id, RequestingUserId = "admin", IsAdmin = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var reloaded = await db.Timesheets.FindAsync(ts.Id);
        Assert.Equal(TimesheetStatus.Submitted, reloaded!.Status);
        Assert.NotNull(reloaded.SubmittedAt);
    }

    [Fact]
    public async Task Rejected_is_resubmitted_not_submitted()
    {
        using var db = TestDb.Create();
        var ts = SeedTimesheet(db, TimesheetStatus.Rejected);

        var result = await SubmitHandler(db).Handle(
            new SubmitTimesheet.Command { Id = ts.Id, RequestingUserId = "admin", IsAdmin = true },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var reloaded = await db.Timesheets.FindAsync(ts.Id);
        Assert.Equal(TimesheetStatus.Resubmitted, reloaded!.Status);
    }

    [Fact]
    public async Task Submitted_is_approved_with_approver_stamp_and_history()
    {
        using var db = TestDb.Create();
        var ts = SeedTimesheet(db, TimesheetStatus.Submitted);

        var result = await StatusHandler(db).Handle(
            new UpdateTimesheetStatus.Command
            {
                Id = ts.Id,
                NewStatus = TimesheetStatus.Approved,
                RequestingUserId = "admin",
                IsAdmin = true,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var reloaded = await db.Timesheets.FindAsync(ts.Id);
        Assert.Equal(TimesheetStatus.Approved, reloaded!.Status);
        Assert.NotNull(reloaded.ApprovedAt);
        Assert.Equal("admin", reloaded.ApproverId);

        var history = await db.TimesheetStatusHistories.SingleAsync(h => h.TimesheetId == ts.Id);
        Assert.Equal((int)TimesheetStatus.Submitted, history.FromStatus);
        Assert.Equal((int)TimesheetStatus.Approved, history.ToStatus);
    }

    [Fact]
    public async Task Submitted_is_rejected_with_a_comment()
    {
        using var db = TestDb.Create();
        var ts = SeedTimesheet(db, TimesheetStatus.Submitted);

        var result = await StatusHandler(db).Handle(
            new UpdateTimesheetStatus.Command
            {
                Id = ts.Id,
                NewStatus = TimesheetStatus.Rejected,
                RequestingUserId = "admin",
                IsAdmin = true,
                Comment = "Hours don't add up",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var reloaded = await db.Timesheets.FindAsync(ts.Id);
        Assert.Equal(TimesheetStatus.Rejected, reloaded!.Status);
    }

    [Fact]
    public async Task Resubmitted_can_be_approved()
    {
        using var db = TestDb.Create();
        var ts = SeedTimesheet(db, TimesheetStatus.Resubmitted);

        var result = await StatusHandler(db).Handle(
            new UpdateTimesheetStatus.Command
            {
                Id = ts.Id,
                NewStatus = TimesheetStatus.Approved,
                RequestingUserId = "admin",
                IsAdmin = true,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var reloaded = await db.Timesheets.FindAsync(ts.Id);
        Assert.Equal(TimesheetStatus.Approved, reloaded!.Status);
    }

    // These two input rules were moved out of the handler into
    // UpdateTimesheetStatusValidator, so they are now asserted at the validator.

    [Fact]
    public void Transition_to_Draft_is_rejected_as_invalid()
    {
        var result = new UpdateTimesheetStatusValidator().Validate(new UpdateTimesheetStatus.Command
        {
            Id = "t1",
            NewStatus = TimesheetStatus.Draft, // only Approved/Rejected are valid here
            RequestingUserId = "admin",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateTimesheetStatus.Command.NewStatus));
    }

    [Fact]
    public void Rejection_without_a_comment_is_refused()
    {
        var result = new UpdateTimesheetStatusValidator().Validate(new UpdateTimesheetStatus.Command
        {
            Id = "t1",
            NewStatus = TimesheetStatus.Rejected,
            RequestingUserId = "admin",
            Comment = null, // required for rejection
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateTimesheetStatus.Command.Comment));
    }

    [Fact]
    public void Approved_transition_passes_validation()
    {
        var result = new UpdateTimesheetStatusValidator().Validate(new UpdateTimesheetStatus.Command
        {
            Id = "t1",
            NewStatus = TimesheetStatus.Approved,
            RequestingUserId = "admin",
        });

        Assert.True(result.IsValid);
    }
}
