using Application.Attendance.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The company dashboard used to recompute each employee's day state four
/// separate times — once in the department rollup, once for the worked-people
/// count, once for late check-ins and once for overtime. It now computes one state
/// per employee into a dictionary and every section reads that.
///
/// The four calls always agreed (they shared a single captured "now"), so this was
/// wasted work rather than a bug. What it did do is spread the aggregation across
/// four loops that each had to remember the same rules — chiefly that someone on
/// approved leave is counted as on leave *instead of* as absent, not as well as.
/// These tests pin the arithmetic that keeps the sections consistent, since
/// collapsing the loops is exactly the kind of change that could quietly
/// double-count somebody.
///
/// Everything asserted here is independent of the time of day the suite runs.
/// The dashboard's <c>Issues</c> list is not — it gates on <c>now.Hour</c> — which
/// is a real testability limit of that section, not something these tests cover.
/// </summary>
public class CompanyAttendanceAggregationTests
{
    private const int EngineeringId = 1;
    private const int SupportId = 2;

    /// <summary>
    /// Events are placed just after midnight UTC rather than at a plausible
    /// working hour, so they always land in today's bucket and always precede
    /// "now" whenever the suite happens to run.
    /// </summary>
    private static DateTime JustAfterMidnight(int minutes) =>
        DateTime.UtcNow.Date.AddMinutes(minutes);

    /// <summary>
    /// Four people, one in each state the rollup distinguishes: working, on a
    /// break, absent, and on approved leave. Two departments, so the per-department
    /// rows have to sum to the headline totals rather than duplicate them.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = EngineeringId, Name = "Engineering", Code = "ENG" });
        db.Departments.Add(new Department { Id = SupportId, Name = "Support", Code = "SUP" });

        AddEmployee(db, "working", EngineeringId);
        AddEmployee(db, "on-break", EngineeringId);
        AddEmployee(db, "absent", SupportId);
        AddEmployee(db, "on-leave", SupportId);

        // Working: checked in, no break, no check-out.
        db.AttendanceEvents.Add(Event("working", 1, AttendanceEventType.CheckIn));

        // On break: checked in, then a break that never ended.
        db.AttendanceEvents.Add(Event("on-break", 1, AttendanceEventType.CheckIn));
        db.AttendanceEvents.Add(Event("on-break", 2, AttendanceEventType.BreakStart));

        // Absent: no events at all.

        // On leave: approved leave spanning right now. Note it also has a check-in,
        // so the rollup has to prefer leave over attendance rather than counting
        // both — the case a four-loop aggregation could get inconsistent.
        db.AttendanceEvents.Add(Event("on-leave", 1, AttendanceEventType.CheckIn));
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "leave-1",
            EmployeeId = "u-on-leave",
            EmployeeProfileId = "p-on-leave",
            DepartmentId = SupportId,
            Status = AnnualLeaveStatus.Approved,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
        });

        db.SaveChanges();
        db.ChangeTracker.Clear();
        return db;
    }

    private static void AddEmployee(AppDbContext db, string key, int departmentId)
    {
        db.Users.Add(new User { Id = $"u-{key}", UserName = key, DisplayName = key });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = $"p-{key}",
            UserId = $"u-{key}",
            DepartmentId = departmentId,
        });
    }

    private static AttendanceEvent Event(string key, int minutes, AttendanceEventType type) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeProfileId = $"p-{key}",
            At = JustAfterMidnight(minutes),
            Type = type,
        };

    private static async Task<Application.Attendance.DTOs.CompanyAttendanceDto> Company(AppDbContext db)
    {
        var result = await new GetCompanyAttendance.Handler(db).Handle(
            new GetCompanyAttendance.Query(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task Every_employee_is_counted_exactly_once()
    {
        using var db = SeedWorld();

        var company = await Company(db);

        Assert.Equal(4, company.Total);
        Assert.Equal(company.Total, company.In + company.Break + company.Out + company.Leave);
    }

    [Fact]
    public async Task Each_state_is_attributed_to_the_right_bucket()
    {
        using var db = SeedWorld();

        var company = await Company(db);

        Assert.Equal(1, company.In);
        Assert.Equal(1, company.Break);
        Assert.Equal(1, company.Out);
        Assert.Equal(1, company.Leave);
    }

    /// <summary>
    /// The one rule all four original loops had to share. Someone on approved
    /// leave who also has a check-in must land in Leave and nowhere else — counted
    /// twice, the headline figures would exceed the headcount.
    /// </summary>
    [Fact]
    public async Task Approved_leave_replaces_the_attendance_count_rather_than_adding_to_it()
    {
        using var db = SeedWorld();

        var company = await Company(db);
        var support = Assert.Single(company.Departments, d => d.Name == "Support");

        Assert.Equal(2, support.Total);
        Assert.Equal(1, support.Leave);
        Assert.Equal(1, support.Out);      // the absent colleague
        Assert.Equal(0, support.In);       // not the person on leave, despite their check-in
        Assert.Equal(0, support.Break);
    }

    [Fact]
    public async Task Department_rows_sum_to_the_headline_totals()
    {
        using var db = SeedWorld();

        var company = await Company(db);

        Assert.Equal(2, company.Departments.Count);
        Assert.Equal(company.Total, company.Departments.Sum(d => d.Total));
        Assert.Equal(company.In, company.Departments.Sum(d => d.In));
        Assert.Equal(company.Break, company.Departments.Sum(d => d.Break));
        Assert.Equal(company.Out, company.Departments.Sum(d => d.Out));
        Assert.Equal(company.Leave, company.Departments.Sum(d => d.Leave));
        Assert.Equal(company.TotalMinutesToday, company.Departments.Sum(d => d.TotalMinutes));
    }

    [Fact]
    public async Task Every_department_row_accounts_for_all_of_its_people()
    {
        using var db = SeedWorld();

        var company = await Company(db);

        Assert.All(company.Departments, d =>
            Assert.Equal(d.Total, d.In + d.Break + d.Out + d.Leave));
    }

    /// <summary>
    /// The rollup groups on <c>p.Department?.Name ?? "Unassigned"</c>, which reads
    /// as though an employee with no department shows up misfiled under
    /// "Unassigned". It does not. EmployeeProfile.DepartmentId is a required FK, so
    /// <c>Include(p =&gt; p.Department)</c> is an inner join and a profile whose
    /// department row is missing is dropped from the query altogether — it vanishes
    /// from the headcount instead.
    ///
    /// Pinned rather than fixed: the "Unassigned" fallback is dead code that looks
    /// live, and the two readings differ in a way that matters if a database ever
    /// does carry a dangling department id. Carried over unchanged from the version
    /// this replaced, which had the identical Include.
    /// </summary>
    [Fact]
    public async Task A_profile_with_a_dangling_department_id_is_dropped_not_grouped_as_unassigned()
    {
        using var db = SeedWorld();
        db.Users.Add(new User { Id = "u-orphan", UserName = "orphan", DisplayName = "orphan" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p-orphan", UserId = "u-orphan", DepartmentId = 99 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var company = await Company(db);

        // The row is there; the dashboard just cannot see it.
        Assert.Equal(5, db.EmployeeProfiles.Count());
        Assert.Equal(4, company.Total);
        Assert.DoesNotContain(company.Departments, d => d.Name == "Unassigned");

        // Whatever it does include still has to add up.
        Assert.Equal(company.Total, company.Departments.Sum(d => d.Total));
    }

    [Fact]
    public async Task An_empty_company_reports_zeroes_rather_than_failing()
    {
        using var db = TestDb.Create();

        var company = await Company(db);

        Assert.Equal(0, company.Total);
        Assert.Equal(0, company.AvgMinutesToday);
        Assert.Empty(company.Departments);
    }
}
