using Application.AnnualLeaves.Commands;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace WorkTrack.Tests;

/// <summary>
/// One employee, one paternity leave type configured 18/5/15, and one ordinary
/// annual-leave type to prove the per-child rules stay off for everything else.
///
/// TestDb (EF in-memory) is the right provider here: these tests turn on the
/// calculator's arithmetic, not on constraints or transactions. ChildCrudTests uses
/// TransactionalTestDb for the parts that do.
/// </summary>
internal static class PerChildLeaveWorld
{
    public const string UserId = "employee-1";
    public const string ProfileId = "profile-1";
    public const int PaternityTypeId = 1;
    public const int AnnualLeaveTypeId = 2;

    public static async Task<AppDbContext> CreateAsync(int leaveYearStartMonth = 1)
    {
        var db = TestDb.Create();

        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = leaveYearStartMonth });

        db.Users.Add(new User
        {
            Id = UserId,
            UserName = "employee-1@example.com",
            Email = "employee-1@example.com",
            DisplayName = "Andreas Georgiou",
        });

        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ProfileId,
            UserId = UserId,
            HasChildren = true,
            // Set so the pooled annual-leave check is live too: a per-child type
            // must not be charged against it, and a 0 here would hide that by
            // switching the pooled check off entirely.
            AnnualLeaveEntitlement = 25,
            LeaveBalance = 25,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = PaternityTypeId,
            Name = "Paternity Leave",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = false,
            DefaultAllowance = 0,
            AllowanceUnit = "weeks/child",
            PerChildEntitlement = true,
            PerChildTotalWeeks = 18,
            PerChildWeeksPerYear = 5,
            ChildEligibleUntilAge = 15,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = AnnualLeaveTypeId,
            Name = "Annual Leave",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = true,
            DefaultAllowance = 25,
        });

        await db.SaveChangesAsync();
        return db;
    }

    public static async Task<Child> AddChildAsync(AppDbContext db, string name, DateOnly dateOfBirth)
    {
        var child = new Child { EmployeeProfileId = ProfileId, Name = name, DateOfBirth = dateOfBirth };
        db.Children.Add(child);
        await db.SaveChangesAsync();
        return child;
    }

    /// <summary>Approved paternity leave already on the record for this child.</summary>
    public static async Task ApproveLeaveAsync(AppDbContext db, string childId, DateTime start, DateTime end)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = UserId,
            EmployeeProfileId = ProfileId,
            ChildId = childId,
            LeaveTypeId = PaternityTypeId,
            StartDate = start,
            EndDate = end,
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>An unsaved request, as a handler holds it before writing.</summary>
    public static AnnualLeave Request(string? childId, DateTime start, DateTime end, int? leaveTypeId = null) => new()
    {
        EmployeeId = UserId,
        EmployeeProfileId = ProfileId,
        ChildId = childId,
        LeaveTypeId = leaveTypeId ?? PaternityTypeId,
        StartDate = start,
        EndDate = end,
        Reason = "Paternity",
        Status = AnnualLeaveStatus.Pending,
    };

    public static async Task<string?> CheckAsync(AppDbContext db, AnnualLeave leave, string? excludeLeaveId = null)
    {
        var profile = await db.EmployeeProfiles.SingleAsync(ep => ep.Id == ProfileId);
        return await PerChildLeaveBalanceCalculator.CheckPerChildEntitlementAsync(
            db, leave, profile, excludeLeaveId, CancellationToken.None);
    }
}
