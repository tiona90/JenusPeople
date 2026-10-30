using Application.Core;
using Application.Reminders;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A leaver is switched off (User.IsActive) rather than deleted, and a switched-off
/// account is nobody's recipient and nobody's subject. The reminders used to read
/// recipients through queries with no IsActive filter, so a deactivated HR
/// Administrator kept receiving the daily attendance report and the birthday
/// digest about colleagues, a deactivated manager the pending-approvals digest, and
/// a leaver "don't forget to check in" every working morning. The manager set a new
/// leave request is routed by had the same gap, so a department whose only manager
/// had left filed Pending against somebody who could never decide it.
/// </summary>
public class DeactivatedAccountRemindersTests
{
    private const int Dept = 1;

    private static readonly AppSettings Enabled = new()
    {
        EmailNotificationsEnabled = true,
        WorkingDays = "custom",
        WorkingDaysCustom = "sun,mon,tue,wed,thu,fri,sat",
    };

    private static AppDbContext SeedWorld()
    {
        var db = TestDb.Create();
        db.Departments.Add(new Department { Id = Dept, Name = "Engineering", Code = "ENG" });

        var adminRole = new Role { Id = "r-admin", Name = AppRoles.SystemAdministrator, NormalizedName = AppRoles.SystemAdministrator.ToUpperInvariant() };
        var hrRole = new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() };
        var managerRole = new Role { Id = "r-mgr", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() };
        db.Roles.AddRange(adminRole, hrRole, managerRole);

        // Birthdays two days out, so both employees fall inside the digest's window.
        var dob = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).AddYears(-30);

        Person(db, "ann", "Ann Active", Dept, active: true, dob: dob, managerProfileId: "max-p");
        Person(db, "leo", "Leo Leaver", Dept, active: false, dob: dob);
        Person(db, "mia", "Mia Manager", Dept, active: true);
        Person(db, "max", "Max Gone-Manager", Dept, active: false);
        Person(db, "hal", "Hal Gone-HR", departmentId: null, active: false);
        Person(db, "sam", "Sam Gone-SysAdmin", departmentId: null, active: false);

        db.UserRoles.AddRange(
            new UserRole { UserId = "mia", RoleId = managerRole.Id },
            new UserRole { UserId = "max", RoleId = managerRole.Id },
            new UserRole { UserId = "hal", RoleId = hrRole.Id },
            new UserRole { UserId = "sam", RoleId = adminRole.Id });
        db.UserDepartments.Add(new UserDepartment { UserId = "hal", DepartmentId = Dept });

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = "ann", EmployeeProfileId = "ann-p", DepartmentId = Dept,
            StartDate = DateTime.UtcNow.AddDays(7), EndDate = DateTime.UtcNow.AddDays(8),
            Status = AnnualLeaveStatus.Pending, CreatedAt = DateTime.UtcNow,
        });

        db.SaveChanges();
        return db;
    }

    private static void Person(AppDbContext db, string id, string name, int? departmentId, bool active, DateOnly? dob = null, string? managerProfileId = null)
    {
        db.Users.Add(new User { Id = id, UserName = id, DisplayName = name, Email = $"{id}@example.com", IsActive = active, DateOfBirth = dob });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = $"{id}-p", UserId = id, DepartmentId = departmentId, ManagerId = managerProfileId,
            AnnualLeaveEntitlement = 20, LeaveBalance = 2,
        });
    }

    private static async Task<FakeEmailService> RunAsync(string reminderId)
    {
        using var db = SeedWorld();
        var email = new FakeEmailService();
        await new ReminderDispatcher(db, email, NullLogger<ReminderDispatcher>.Instance)
            .DispatchAsync(reminderId, Enabled, CancellationToken.None);
        return email;
    }

    private static IEnumerable<string> Recipients(FakeEmailService email) => email.Sent.Select(m => m.Recipient);

    [Fact]
    public async Task A_leaver_is_not_reminded_to_check_in()
    {
        var email = await RunAsync(ReminderDispatcher.CheckInReminder);

        Assert.Contains("ann@example.com", Recipients(email));
        Assert.DoesNotContain("leo@example.com", Recipients(email));
        Assert.DoesNotContain("max@example.com", Recipients(email));
    }

    [Fact]
    public async Task A_leaver_is_not_told_their_balance_is_low()
    {
        var email = await RunAsync(ReminderDispatcher.LowBalance);

        Assert.Contains("ann@example.com", Recipients(email));
        Assert.DoesNotContain("leo@example.com", Recipients(email));
    }

    [Fact]
    public async Task The_birthday_digest_reaches_no_deactivated_account_and_names_no_leaver()
    {
        var email = await RunAsync(ReminderDispatcher.BirthdayReminder);

        Assert.DoesNotContain("max@example.com", Recipients(email));
        Assert.DoesNotContain("hal@example.com", Recipients(email));
        Assert.DoesNotContain("sam@example.com", Recipients(email));
        var toMia = Assert.Single(email.Sent, m => m.Recipient == "mia@example.com");
        Assert.Contains("Ann Active", toMia.HtmlBody);
        Assert.DoesNotContain("Leo Leaver", toMia.HtmlBody);
    }

    [Fact]
    public async Task The_pending_approvals_digest_skips_a_deactivated_manager()
    {
        var email = await RunAsync(ReminderDispatcher.PendingApprovals);

        Assert.Contains("mia@example.com", Recipients(email));
        Assert.DoesNotContain("max@example.com", Recipients(email));
    }

    [Fact]
    public async Task A_deactivated_manager_is_not_among_a_request_s_managers()
    {
        using var db = SeedWorld();
        var ann = db.EmployeeProfiles.Single(p => p.Id == "ann-p");

        // max is both ann's direct manager and a Manager in her department.
        var managers = await ManagerNotificationRecipients.ResolveAsync(db, ann, CancellationToken.None);

        Assert.Equal(["mia"], managers.Select(m => m.UserId));
    }
}
