using Application.Timesheets.Commands;
using Application.Timesheets.Queries;
using Application.Timesheets.Validators;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The HR Administrator takes a timesheet approval back, the way they cancel an
/// approved leave: the sheet returns to Rejected for the employee to correct and
/// resubmit, the approval stamp is cleared, the history says why, and the employee
/// and the managers are told. HR alone, inside their departments, on an approved sheet,
/// with a reason.
/// </summary>
public class ReopenTimesheetTests
{
    private const int Dept = 1;
    private const string Employee = "u-emp";
    private const string EmployeeProfile = "p-emp";
    private const string Manager = "u-mgr";
    private const string Hr = "u-hr";

    private static async Task<AppDbContext> WorldAsync()
    {
        var db = TestDb.Create();
        db.Departments.AddRange(
            new Department { Id = Dept, Name = "Finance", Code = "FIN" },
            new Department { Id = 2, Name = "Sales", Code = "SAL" });
        db.Roles.AddRange(
            new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() },
            new Role { Id = "r-mgr", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() });
        db.Users.AddRange(
            new User { Id = Employee, UserName = Employee, Email = "emp@t.local", DisplayName = "Employee 2C", IsActive = true },
            new User { Id = Manager, UserName = Manager, Email = "mgr@t.local", DisplayName = "Theodoros Iona", IsActive = true },
            new User { Id = Hr, UserName = Hr, Email = "hr@t.local", DisplayName = "Helen HR", IsActive = true });
        db.UserRoles.AddRange(
            new UserRole { UserId = Hr, RoleId = "r-hr" },
            new UserRole { UserId = Manager, RoleId = "r-mgr" });
        db.EmployeeProfiles.AddRange(
            new EmployeeProfile { Id = EmployeeProfile, UserId = Employee, DepartmentId = Dept },
            new EmployeeProfile { Id = "p-mgr", UserId = Manager, DepartmentId = Dept },
            new EmployeeProfile { Id = "p-hr", UserId = Hr, DepartmentId = null });
        db.UserDepartments.Add(new UserDepartment { UserId = Hr, DepartmentId = Dept });
        db.Timesheets.Add(new Timesheet
        {
            Id = "ts", EmployeeProfileId = EmployeeProfile, DepartmentId = Dept,
            PeriodStart = new DateTime(2026, 8, 17), PeriodEnd = new DateTime(2026, 8, 23),
            TotalHours = 29.5m, Status = TimesheetStatus.Approved,
            SubmittedAt = new DateTime(2026, 8, 20), ApprovedAt = new DateTime(2026, 8, 21), ApproverId = Manager,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    private static Task<Application.Core.Result<MediatR.Unit>> ReopenAsync(
        AppDbContext db, FakeEmailService email, string byUserId, bool asHr, string? comment = "Hours booked to the wrong project", string id = "ts") =>
        new ReopenTimesheet.Handler(db, email, NullLogger<ReopenTimesheet.Handler>.Instance).Handle(
            new ReopenTimesheet.Command { Id = id, RequestingUserId = byUserId, IsHrAdministrator = asHr, Comment = comment },
            CancellationToken.None);

    [Fact]
    public async Task Hr_sends_an_approved_timesheet_back_for_review()
    {
        using var db = await WorldAsync();
        var email = new FakeEmailService();

        var result = await ReopenAsync(db, email, Hr, asHr: true);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await db.Timesheets.AsNoTracking().FirstAsync(t => t.Id == "ts");
        Assert.Equal(TimesheetStatus.Rejected, stored.Status);
        Assert.Null(stored.ApprovedAt);
        Assert.Null(stored.ApproverId);
        Assert.NotNull(stored.SubmittedAt);

        var history = await db.TimesheetStatusHistories.AsNoTracking().SingleAsync(h => h.TimesheetId == "ts");
        Assert.Equal((int)TimesheetStatus.Approved, history.FromStatus);
        Assert.Equal((int)TimesheetStatus.Rejected, history.ToStatus);
        Assert.Equal(Hr, history.ChangedByUserId);
        Assert.Equal("Hours booked to the wrong project", history.Comment);

        var toEmployee = Assert.Single(email.Sent, m => m.Recipient == "emp@t.local");
        Assert.Equal("Your timesheet approval was cancelled", toEmployee.Subject);
        Assert.Contains("Hours booked to the wrong project", toEmployee.HtmlBody);
        Assert.Contains("correct and resubmit", toEmployee.HtmlBody);
        var toManager = Assert.Single(email.Sent, m => m.Recipient == "mgr@t.local");
        Assert.Contains("once they resubmit", toManager.HtmlBody);
    }

    /// <summary>
    /// Back with the employee, not the manager: it is out of the review queue until
    /// they resubmit, and resubmitting sends it back through review as Resubmitted.
    /// </summary>
    [Fact]
    public async Task The_employee_can_resubmit_a_sent_back_timesheet()
    {
        using var db = await WorldAsync();
        Assert.True((await ReopenAsync(db, new FakeEmailService(), Hr, asHr: true)).IsSuccess);

        var page = await new GetTimesheetList.Handler(db).Handle(
            new GetTimesheetList.Query { RequestingUserId = Hr, IsAdmin = false, IsManager = true, IsHrAdministrator = true },
            CancellationToken.None);
        Assert.False(page.Items.Single(t => t.Id == "ts").AwaitingManager);

        db.ChangeTracker.Clear();
        var email = new FakeEmailService();
        var submitted = await new SubmitTimesheet.Handler(db, email, NullLogger<SubmitTimesheet.Handler>.Instance).Handle(
            new SubmitTimesheet.Command { Id = "ts", RequestingUserId = Employee },
            CancellationToken.None);

        Assert.True(submitted.IsSuccess, submitted.Error);
        Assert.Equal(TimesheetStatus.Resubmitted, (await db.Timesheets.AsNoTracking().FirstAsync(t => t.Id == "ts")).Status);
        Assert.Contains(email.Sent, m => m.Recipient == "mgr@t.local");
    }

    [Fact]
    public async Task A_manager_cannot_send_one_back()
    {
        using var db = await WorldAsync();
        var email = new FakeEmailService();

        var result = await ReopenAsync(db, email, Manager, asHr: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReopenTimesheet.HrOnlyMessage, result.Error);
        Assert.Equal(TimesheetStatus.Approved, (await db.Timesheets.AsNoTracking().FirstAsync(t => t.Id == "ts")).Status);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Only_an_approved_timesheet_can_be_sent_back()
    {
        using var db = await WorldAsync();
        (await db.Timesheets.FirstAsync(t => t.Id == "ts")).Status = TimesheetStatus.Submitted;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await ReopenAsync(db, new FakeEmailService(), Hr, asHr: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReopenTimesheet.NotApprovedMessage, result.Error);
    }

    [Fact]
    public async Task Hr_is_held_to_their_assigned_departments()
    {
        using var db = await WorldAsync();
        var sheet = await db.Timesheets.FirstAsync(t => t.Id == "ts");
        sheet.DepartmentId = 2;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await ReopenAsync(db, new FakeEmailService(), Hr, asHr: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ReopenTimesheet.OutOfScopeMessage, result.Error);
    }

    [Fact]
    public void A_reason_is_required()
    {
        var validator = new ReopenTimesheetValidator();

        var missing = validator.Validate(new ReopenTimesheet.Command { Id = "ts", RequestingUserId = Hr, IsHrAdministrator = true, Comment = "   " });
        var given = validator.Validate(new ReopenTimesheet.Command { Id = "ts", RequestingUserId = Hr, IsHrAdministrator = true, Comment = "Wrong project" });

        Assert.Contains(missing.Errors, e => e.ErrorMessage == ReopenTimesheet.ReasonRequiredMessage);
        Assert.True(given.IsValid);
    }
}
