using Application.TimesheetStatusHistories.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Regression for the manager-scope gap in GetTimesheetStatusHistoryList: the
/// IsManager branch previously had no department filter (a Manager saw every
/// timesheet's status history). A manager must only see history for their own
/// timesheets, their managed departments, and their direct reports.
/// </summary>
public class TimesheetStatusHistoryScopeTests
{
    private const string ManagerUserId = "mgr-u";

    private static void SeedWorld(AppDbContext db)
    {
        // Manager in department 1.
        db.Users.Add(new User { Id = ManagerUserId, UserName = "mgr", Email = "mgr@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mgr-p", UserId = ManagerUserId, DepartmentId = 1 });

        // Employee in the manager's department (in scope).
        db.Users.Add(new User { Id = "in-u", UserName = "in", Email = "in@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "in-p", UserId = "in-u", DepartmentId = 1, ManagerId = "mgr-p" });

        // Employee in a different department, not a report (out of scope).
        db.Users.Add(new User { Id = "out-u", UserName = "out", Email = "out@test.local" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "out-p", UserId = "out-u", DepartmentId = 2 });

        db.Timesheets.Add(new Timesheet { Id = "ts-in", EmployeeId = "in-p", DepartmentId = 1, PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7), Status = TimesheetStatus.Approved });
        db.Timesheets.Add(new Timesheet { Id = "ts-out", EmployeeId = "out-p", DepartmentId = 2, PeriodStart = new DateTime(2024, 1, 1), PeriodEnd = new DateTime(2024, 1, 7), Status = TimesheetStatus.Approved });

        db.TimesheetStatusHistories.Add(new TimesheetStatusHistory { Id = "h-in", TimesheetId = "ts-in", ChangedByUserId = ManagerUserId, FromStatus = 1, ToStatus = 2 });
        db.TimesheetStatusHistories.Add(new TimesheetStatusHistory { Id = "h-out", TimesheetId = "ts-out", ChangedByUserId = ManagerUserId, FromStatus = 1, ToStatus = 2 });

        db.SaveChanges();
    }

    [Fact]
    public async Task Manager_only_sees_history_within_their_scope()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(
            new GetTimesheetStatusHistoryList.Query
            {
                RequestingUserId = ManagerUserId,
                IsAdmin = false,
                IsManager = true,
            },
            CancellationToken.None);

        var timesheetIds = result.Items.Select(h => h.TimesheetId).ToList();
        Assert.Contains("ts-in", timesheetIds);
        Assert.DoesNotContain("ts-out", timesheetIds); // outside the manager's department
    }

    [Fact]
    public async Task Admin_sees_all_history()
    {
        using var db = TestDb.Create();
        SeedWorld(db);

        var result = await new GetTimesheetStatusHistoryList.Handler(db).Handle(
            new GetTimesheetStatusHistoryList.Query
            {
                RequestingUserId = "someone",
                IsAdmin = true,
            },
            CancellationToken.None);

        var timesheetIds = result.Items.Select(h => h.TimesheetId).ToList();
        Assert.Contains("ts-in", timesheetIds);
        Assert.Contains("ts-out", timesheetIds);
    }
}
