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
/// (UpdateTimesheetStatus). System Administrator acts to keep the focus on the transition rules
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

    /// <summary>
    /// A Manager's own timesheet sits inside their own department scope, and an HR
    /// Administrator reaches their own department-less one — so scope alone would
    /// let either sign off their own hours. The approval pages leave the caller's
    /// own row out of the queue; this pins the rule behind that on the server.
    /// </summary>
    [Theory]
    [InlineData(TimesheetStatus.Approved, false, true, false)]  // a Manager, their own
    [InlineData(TimesheetStatus.Rejected, false, true, false)]
    [InlineData(TimesheetStatus.Approved, false, true, true)]   // an HR Administrator, their own
    [InlineData(TimesheetStatus.Approved, true, false, false)]  // even the unscoped admin flag
    public async Task Nobody_decides_their_own_timesheet(TimesheetStatus target, bool isAdmin, bool isManager, bool isHr)
    {
        using var db = TestDb.Create();
        db.Roles.Add(new Role { Id = "r-mgr", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() });
        db.UserRoles.Add(new UserRole { UserId = "emp-user-1", RoleId = "r-mgr" });
        var ts = SeedTimesheet(db, TimesheetStatus.Submitted);

        var result = await StatusHandler(db).Handle(
            new UpdateTimesheetStatus.Command
            {
                Id = ts.Id,
                NewStatus = target,
                RequestingUserId = "emp-user-1",
                IsAdmin = isAdmin,
                IsManager = isManager,
                IsHrAdministrator = isHr,
                Comment = "Looks fine",
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateTimesheetStatus.OwnTimesheetMessage, result.Error);
        Assert.Equal(TimesheetStatus.Submitted, (await db.Timesheets.FindAsync(ts.Id))!.Status);
        Assert.False(await db.TimesheetStatusHistories.AnyAsync());
    }

    /// <summary>
    /// A reviewer decides only what is in front of them for review. Approving a
    /// Draft locked the employee out of a week they were still filling in;
    /// rejecting an Approved one took an approval back without HR's
    /// ReopenTimesheet; re-approving an Approved one re-stamped and re-emailed.
    /// </summary>
    [Theory]
    [InlineData(TimesheetStatus.Draft, TimesheetStatus.Approved)]
    [InlineData(TimesheetStatus.Draft, TimesheetStatus.Rejected)]
    [InlineData(TimesheetStatus.Approved, TimesheetStatus.Rejected)]
    [InlineData(TimesheetStatus.Approved, TimesheetStatus.Approved)]
    [InlineData(TimesheetStatus.Rejected, TimesheetStatus.Approved)]
    public async Task Only_a_submitted_sheet_is_approved_or_rejected(TimesheetStatus from, TimesheetStatus target)
    {
        using var db = TestDb.Create();
        var ts = SeedTimesheet(db, from);

        var result = await StatusHandler(db).Handle(
            new UpdateTimesheetStatus.Command
            {
                Id = ts.Id, NewStatus = target, RequestingUserId = "admin", IsAdmin = true, Comment = "Reason",
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(UpdateTimesheetStatus.NotAwaitingReviewMessage, result.Error);
        Assert.Equal(from, (await db.Timesheets.FindAsync(ts.Id))!.Status);
        Assert.False(await db.TimesheetStatusHistories.AnyAsync());
    }

    /// <summary>
    /// Submitting is a Draft's or a Rejected sheet's next step, nothing else's: a
    /// repeat submit re-sent every email, and submitting an Approved sheet put it
    /// back in the queue with its approval stamp still on it.
    /// </summary>
    [Theory]
    [InlineData(TimesheetStatus.Submitted)]
    [InlineData(TimesheetStatus.Resubmitted)]
    [InlineData(TimesheetStatus.Approved)]
    public async Task Only_a_draft_or_rejected_sheet_is_submitted(TimesheetStatus from)
    {
        using var db = TestDb.Create();
        var ts = SeedTimesheet(db, from);

        var result = await SubmitHandler(db).Handle(
            new SubmitTimesheet.Command { Id = ts.Id, RequestingUserId = "admin", IsAdmin = true },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(SubmitTimesheet.AlreadySubmittedMessage, result.Error);
        Assert.Equal(from, (await db.Timesheets.FindAsync(ts.Id))!.Status);
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
