using Application.Reminders;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The pending-approvals digest mailed every System Administrator an organisation-wide summary
/// of leave and timesheets awaiting review, on top of the department summary it
/// mails each Manager. The people who action a submission are the managers, so
/// the System Administrator's copy was noise arriving every morning about queues that are not
/// theirs. The digest now goes to managers only. System Administrators are seeded with a real
/// UserRole row, which is what the recipient lookup reads.
/// </summary>
public class PendingApprovalsDigestExcludesAdminsTests
{
    private const string AdminEmail = "ada@example.com";
    private const string ManagerEmail = "mia@example.com";
    private const int DepartmentId = 1;

    // Every day works, so the digest runs whatever day the test executes on
    // (nothing is dispatched on a non-working day).
    private static readonly AppSettings Enabled = new()
    {
        EmailNotificationsEnabled = true,
        WorkingDays = "custom",
        WorkingDaysCustom = "sun,mon,tue,wed,thu,fri,sat",
    };

    /// <summary>
    /// One System Administrator, one Manager of Engineering, one Engineering employee with a
    /// pending leave request and a submitted timesheet.
    /// </summary>
    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });

        var adminRole = new Role { Id = "r-admin", Name = AppRoles.SystemAdministrator, NormalizedName = AppRoles.SystemAdministrator.ToUpperInvariant() };
        var managerRole = new Role { Id = "r-manager", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() };
        db.Roles.AddRange(adminRole, managerRole);

        SeedProfile(db, "admin-u", "admin-p", "Ada System Administrator", AdminEmail, departmentId: null);
        db.UserRoles.Add(new UserRole { UserId = "admin-u", RoleId = adminRole.Id });

        SeedProfile(db, "manager-u", "manager-p", "Mia Manager", ManagerEmail, DepartmentId);
        db.UserRoles.Add(new UserRole { UserId = "manager-u", RoleId = managerRole.Id });

        SeedProfile(db, "employee-u", "employee-p", "Eve Employee", "eve@example.com", DepartmentId);

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = "employee-u",
            EmployeeProfileId = "employee-p",
            DepartmentId = DepartmentId,
            StartDate = DateTime.UtcNow.AddDays(7),
            EndDate = DateTime.UtcNow.AddDays(8),
            Status = AnnualLeaveStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });
        db.Timesheets.Add(new Timesheet
        {
            EmployeeProfileId = "employee-p",
            DepartmentId = DepartmentId,
            PeriodStart = DateTime.UtcNow.AddDays(-7),
            PeriodEnd = DateTime.UtcNow.AddDays(-1),
            Status = TimesheetStatus.Submitted,
        });

        db.SaveChanges();
        return db;
    }

    private static void SeedProfile(AppDbContext db, string userId, string profileId, string displayName, string email, int? departmentId)
    {
        db.Users.Add(new User { Id = userId, UserName = userId, DisplayName = displayName, Email = email });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = profileId, UserId = userId, DepartmentId = departmentId });
    }

    private static ReminderDispatcher DispatcherFor(AppDbContext db, FakeEmailService email) =>
        new(db, email, NullLogger<ReminderDispatcher>.Instance);

    [Fact]
    public async Task Digest_is_not_sent_to_an_admin()
    {
        using var db = SeedWorld();
        var email = new FakeEmailService();

        await DispatcherFor(db, email).DispatchAsync(ReminderDispatcher.PendingApprovals, Enabled, CancellationToken.None);

        Assert.DoesNotContain(AdminEmail, email.Sent.Select(m => m.Recipient));
    }

    [Fact]
    public async Task Digest_still_reaches_the_department_manager()
    {
        using var db = SeedWorld();
        var email = new FakeEmailService();

        await DispatcherFor(db, email).DispatchAsync(ReminderDispatcher.PendingApprovals, Enabled, CancellationToken.None);

        var toManager = Assert.Single(email.Sent, m => m.Recipient == ManagerEmail);
        Assert.Contains("your department", toManager.HtmlBody);
        Assert.Contains("<strong>1</strong> leave request(s)", toManager.HtmlBody);
        Assert.Contains("<strong>1</strong> timesheet(s)", toManager.HtmlBody);
    }

    private const string HrAdminEmail = "hazel@example.com";

    /// <summary>
    /// One Manager of Engineering with a Pending leave in their department, and one
    /// HR Administrator assigned to Engineering with a separate row already
    /// AwaitingHrApproval in the same department. Each stage's digest counts only
    /// its own row.
    /// </summary>
    private static AppDbContext SeedWorldWithHrStage()
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });

        var managerRole = new Role { Id = "r-manager", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() };
        var hrRole = new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() };
        db.Roles.AddRange(managerRole, hrRole);

        SeedProfile(db, "manager-u", "manager-p", "Mia Manager", ManagerEmail, DepartmentId);
        db.UserRoles.Add(new UserRole { UserId = "manager-u", RoleId = managerRole.Id });

        db.Users.Add(new User { Id = "hr-u", UserName = "hr-u", DisplayName = "Hazel HR", Email = HrAdminEmail });
        db.UserRoles.Add(new UserRole { UserId = "hr-u", RoleId = hrRole.Id });
        db.UserDepartments.Add(new UserDepartment { UserId = "hr-u", DepartmentId = DepartmentId });

        SeedProfile(db, "employee-u", "employee-p", "Eve Employee", "eve@example.com", DepartmentId);

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = "employee-u",
            EmployeeProfileId = "employee-p",
            DepartmentId = DepartmentId,
            StartDate = DateTime.UtcNow.AddDays(7),
            EndDate = DateTime.UtcNow.AddDays(8),
            Status = AnnualLeaveStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        });
        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = "employee-u",
            EmployeeProfileId = "employee-p",
            DepartmentId = DepartmentId,
            StartDate = DateTime.UtcNow.AddDays(14),
            EndDate = DateTime.UtcNow.AddDays(15),
            Status = AnnualLeaveStatus.AwaitingHrApproval,
            CreatedAt = DateTime.UtcNow,
        });

        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Manager_and_hr_digests_each_count_only_their_own_stage()
    {
        using var db = SeedWorldWithHrStage();
        var email = new FakeEmailService();

        await DispatcherFor(db, email).DispatchAsync(ReminderDispatcher.PendingApprovals, Enabled, CancellationToken.None);

        var toManager = Assert.Single(email.Sent, m => m.Recipient == ManagerEmail);
        Assert.Contains("<strong>1</strong> leave request(s)", toManager.HtmlBody);

        var toHr = Assert.Single(email.Sent, m => m.Recipient == HrAdminEmail);
        Assert.Contains("your departments", toHr.HtmlBody);
        Assert.Contains("<strong>1</strong> leave request(s)", toHr.HtmlBody);
    }
}
