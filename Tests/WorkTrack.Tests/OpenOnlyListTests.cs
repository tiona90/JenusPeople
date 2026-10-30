using Application.AnnualLeaves.Queries;
using Application.Timesheets.Queries;
using AutoMapper;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The notification bell lists only what is waiting on a decision, but it polled
/// the full, unpaged leave and timesheet lists — every row in scope, for all time,
/// timesheets with every entry — to find it. OpenOnly narrows both lists to the
/// open rows in SQL, inside the same scope as before.
/// </summary>
public class OpenOnlyListTests
{
    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<Application.Core.MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task Open_only_leave_is_pending_or_with_hr()
    {
        using var db = TestDb.Create();
        db.Users.Add(new User { Id = "u1", UserName = "u1", Email = "u1@t.local" });
        var i = 0;
        foreach (var status in Enum.GetValues<AnnualLeaveStatus>())
        {
            db.AnnualLeaves.Add(new AnnualLeave
            {
                Id = $"L{i}", EmployeeId = "u1", Status = status, Reason = "x",
                StartDate = new DateTime(2024, 3, 4).AddDays(7 * i), EndDate = new DateTime(2024, 3, 4).AddDays(7 * i++),
            });
        }
        await db.SaveChangesAsync();

        var result = await new GetAnnualLeaveList.Handler(db, Mapper()).Handle(
            new GetAnnualLeaveList.Query { RequestingUserId = "admin", IsAdmin = true, OpenOnly = true }, CancellationToken.None);

        Assert.Equal(
            new[] { "AwaitingHrApproval", "Pending" },
            result.Items.Select(l => l.Status.ToString()).OrderBy(s => s));
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task Open_only_timesheets_are_submitted_or_resubmitted()
    {
        using var db = TestDb.Create();
        db.Users.Add(new User { Id = "u1", UserName = "u1", Email = "u1@t.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = 1 });
        var i = 0;
        foreach (var status in Enum.GetValues<TimesheetStatus>())
        {
            var start = new DateTime(2024, 1, 1).AddDays(7 * i++);
            db.Timesheets.Add(new Timesheet { EmployeeProfileId = "p1", DepartmentId = 1, Status = status, PeriodStart = start, PeriodEnd = start.AddDays(6) });
        }
        await db.SaveChangesAsync();

        var result = await new GetTimesheetList.Handler(db).Handle(
            new GetTimesheetList.Query { RequestingUserId = "admin", IsAdmin = true, OpenOnly = true }, CancellationToken.None);

        Assert.Equal(
            new[] { "Resubmitted", "Submitted" },
            result.Items.Select(t => t.Status).OrderBy(s => s));
    }

    [Fact]
    public async Task Without_the_flag_every_row_is_listed_as_before()
    {
        using var db = TestDb.Create();
        db.Users.Add(new User { Id = "u1", UserName = "u1", Email = "u1@t.local" });
        db.AnnualLeaves.Add(new AnnualLeave { Id = "L1", EmployeeId = "u1", Status = AnnualLeaveStatus.Approved, Reason = "x", StartDate = new DateTime(2024, 3, 4), EndDate = new DateTime(2024, 3, 4) });
        db.AnnualLeaves.Add(new AnnualLeave { Id = "L2", EmployeeId = "u1", Status = AnnualLeaveStatus.Pending, Reason = "x", StartDate = new DateTime(2024, 3, 11), EndDate = new DateTime(2024, 3, 11) });
        await db.SaveChangesAsync();

        var result = await new GetAnnualLeaveList.Handler(db, Mapper()).Handle(
            new GetAnnualLeaveList.Query { RequestingUserId = "admin", IsAdmin = true }, CancellationToken.None);

        Assert.Equal(2, result.Total);
    }
}
