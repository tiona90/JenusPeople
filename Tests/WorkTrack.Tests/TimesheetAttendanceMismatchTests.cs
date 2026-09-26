using Application.Attendance.Support;
using Application.Timesheets.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Each day of a timesheet is compared with the time attendance recorded for it.
/// A difference beyond the 15-minute grace is shown to the reviewer; a day of
/// approved leave is never a mismatch; a day still open is not judged. Flag only:
/// nothing about submitting or approving changes.
/// </summary>
public class TimesheetAttendanceMismatchTests
{
    private static readonly DateTime Mon = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc); // Saturday

    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC", WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00",
            BreakMode = "flexible", BreakMinutes = 60, WorkingDays = "mon-fri",
        });
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = 1, Name = "Alpha", Code = "ALP" });
        db.Users.Add(new User { Id = "u-tia", UserName = "tia", DisplayName = "Tia Timesheet" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p-tia", UserId = "u-tia", DepartmentId = 1 });

        // Attendance: Mon 8h, Tue 6h30, Wed on leave, Thu nothing, Fri nothing.
        Worked(db, Mon, 8, 16);
        Worked(db, Mon.AddDays(1), 8, 14, 30);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "l-1", EmployeeId = "u-tia", EmployeeProfileId = "p-tia", DepartmentId = 1,
            StartDate = Mon.AddDays(2), EndDate = Mon.AddDays(2), Status = AnnualLeaveStatus.Approved,
        });

        // Timesheet: Mon 8, Tue 8, Wed 4, Thu 8, Fri 0.
        var sheet = new Timesheet
        {
            Id = "ts-1", EmployeeProfileId = "p-tia", DepartmentId = 1,
            PeriodStart = new DateTime(2026, 9, 21), PeriodEnd = new DateTime(2026, 9, 27),
            Status = TimesheetStatus.Submitted, TotalHours = 28,
        };
        db.Timesheets.Add(sheet);
        Entry(db, 0, 8m);
        Entry(db, 1, 8m);
        Entry(db, 2, 4m);
        Entry(db, 3, 8m);

        // An old sheet, outside the comparison window.
        db.Timesheets.Add(new Timesheet
        {
            Id = "ts-old", EmployeeProfileId = "p-tia", DepartmentId = 1,
            PeriodStart = new DateTime(2026, 5, 4), PeriodEnd = new DateTime(2026, 5, 10),
            Status = TimesheetStatus.Approved, TotalHours = 0,
        });

        db.SaveChanges();
        return db;
    }

    private static void Worked(AppDbContext db, DateTime day, int inHour, int outHour, int outMinute = 0)
    {
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-tia", day.AddHours(inHour), AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-tia", day.AddHours(outHour).AddMinutes(outMinute), AttendanceEventType.CheckOut));
    }

    private static void Entry(AppDbContext db, int dayOffset, decimal hours) =>
        db.TimesheetEntries.Add(new TimesheetEntry
        {
            TimesheetId = "ts-1", ProjectId = 1, Date = new DateTime(2026, 9, 21).AddDays(dayOffset), HoursWorked = hours,
        });

    private static async Task<List<Application.Timesheets.DTOs.TimesheetDto>> List(AppDbContext db, bool includeAttendance = true)
    {
        var result = await new GetTimesheetList.Handler(db).Handle(
            new GetTimesheetList.Query
            {
                RequestingUserId = "nobody", IsAdmin = true, NowUtc = Now,
                IncludeAttendanceComparison = includeAttendance,
            },
            CancellationToken.None);
        return result.Items;
    }

    [Fact]
    public async Task Each_day_carries_attendance_and_the_mismatch_beyond_the_grace()
    {
        using var db = SeedWorld();
        var ts = Assert.Single(await List(db), t => t.Id == "ts-1");

        Assert.Equal([480, 390, 0, 0, 0], ts.AttendanceMinutes);
        Assert.Equal([null, 90, null, 480, null], ts.DayMismatchMinutes);
        Assert.Equal([false, false, true, false, false], ts.OnLeaveDays);
        Assert.Equal(2, ts.MismatchDayCount);
    }

    [Fact]
    public async Task A_sheet_older_than_the_window_is_not_compared()
    {
        using var db = SeedWorld();
        var old = Assert.Single(await List(db), t => t.Id == "ts-old");

        Assert.Null(old.AttendanceMinutes);
        Assert.Null(old.DayMismatchMinutes);
        Assert.Null(old.OnLeaveDays);
        Assert.Equal(0, old.MismatchDayCount);
    }

    [Fact]
    public async Task Today_is_not_judged_while_it_is_open()
    {
        using var db = SeedWorld();
        // Friday is "today"; Tia is checked in with 2h so far and logged 8h.
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-tia", Mon.AddDays(4).AddHours(8), AttendanceEventType.CheckIn));
        Entry(db, 4, 8m);
        db.SaveChanges();

        var result = await new GetTimesheetList.Handler(db).Handle(
            new GetTimesheetList.Query
            {
                RequestingUserId = "nobody", IsAdmin = true, NowUtc = Mon.AddDays(4).AddHours(10),
                IncludeAttendanceComparison = true,
            },
            CancellationToken.None);
        var ts = Assert.Single(result.Items, t => t.Id == "ts-1");

        Assert.Null(ts.DayMismatchMinutes![4]);
    }

    [Fact]
    public async Task Without_the_flag_nothing_is_compared()
    {
        // The bell polls the unpaged list every 15 seconds and renders no chip, so
        // the comparison is opt-in: the pages that show it ask for it.
        using var db = SeedWorld();
        var ts = Assert.Single(await List(db, includeAttendance: false), t => t.Id == "ts-1");

        Assert.Null(ts.AttendanceMinutes);
        Assert.Null(ts.DayMismatchMinutes);
        Assert.Null(ts.OnLeaveDays);
        Assert.Equal(0, ts.MismatchDayCount);
    }

    [Fact]
    public async Task A_past_day_never_checked_out_is_not_compared()
    {
        using var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings
        {
            TimeZoneId = "UTC", WorkingHoursStart = "08:00", WorkingHoursEnd = "17:00",
            BreakMode = "flexible", BreakMinutes = 60, WorkingDays = "mon-fri",
        });
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Projects.Add(new Project { Id = 1, Name = "Alpha", Code = "ALP" });
        db.Users.Add(new User { Id = "u-tia", UserName = "tia", DisplayName = "Tia Timesheet" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p-tia", UserId = "u-tia", DepartmentId = 1 });
        db.Timesheets.Add(new Timesheet
        {
            Id = "ts-1", EmployeeProfileId = "p-tia", DepartmentId = 1,
            PeriodStart = new DateTime(2026, 9, 21), PeriodEnd = new DateTime(2026, 9, 27),
            Status = TimesheetStatus.Submitted, TotalHours = 8,
        });
        Entry(db, 0, 8m);
        // Monday: checked in at 08:00 and never checked out. Run to Saturday noon,
        // that would read as 124 hours attended.
        db.AttendanceEvents.Add(AttendanceDay.NewEvent("p-tia", Mon.AddHours(8), AttendanceEventType.CheckIn));
        db.SaveChanges();

        var ts = Assert.Single(await List(db), t => t.Id == "ts-1");

        Assert.Null(ts.DayMismatchMinutes![0]);
        Assert.Equal(0, ts.AttendanceMinutes![0]);
        Assert.Equal(0, ts.MismatchDayCount);
    }
}
