using Application.EmployeeProfiles.Commands;
using Application.EmployeeProfiles.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Leave stopped being configured per person: the annual-leave allowance on Leave
/// Types is what everyone gets, and the server writes `AnnualLeaveEntitlement` and
/// `LeaveBalance` from it (<see cref="Application.AdminUsers.Commands.CreateAdminUser"/>
/// on hire, <see cref="Application.LeaveTypes.Commands.UpdateLeaveType"/> when the
/// allowance moves).
///
/// Editing a user must therefore leave both columns alone. It used to assign them
/// straight from the request, which had two bad endings once the dialog stopped
/// showing the field: a stale figure echoed back overrides the allowance for that one
/// employee, and an omitted field arrives as 0 — which switches their approval-time
/// balance check off outright (see AnnualLeaveBalanceCalculator, entitlement &lt;= 0).
/// </summary>
public class EmployeeProfileLeaveNumbersTests
{
    private static async Task<(AppDbContext Db, string ProfileId)> SeedAsync()
    {
        var db = TestDb.Create();
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Departments.Add(new Department { Id = 2, Name = "Support", Code = "SUP" });

        var profile = new EmployeeProfile
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "u-1",
            DepartmentId = 1,
            JobTitle = "Engineer",
            AnnualLeaveEntitlement = 25,
            LeaveBalance = 18,
        };
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return (db, profile.Id);
    }

    private static Task<Application.Core.Result<MediatR.Unit>> Handle(
        AppDbContext db, EditEmployeeProfileRequest request) =>
        new EditEmployeeProfile.Handler(db)
            .Handle(new EditEmployeeProfile.Command { EmployeeProfile = request }, CancellationToken.None);

    [Fact]
    public async Task Editing_a_profile_leaves_its_leave_numbers_untouched()
    {
        var (db, profileId) = await SeedAsync();

        var result = await Handle(db, new EditEmployeeProfileRequest
        {
            Id = profileId,
            DepartmentId = 2,
            ManagerId = null,
            JobTitle = "Senior Engineer",
        });

        Assert.True(result.IsSuccess, result.Error);
        var saved = await db.EmployeeProfiles.AsNoTracking().SingleAsync(ep => ep.Id == profileId);
        // What the edit was for:
        Assert.Equal(2, saved.DepartmentId);
        Assert.Equal("Senior Engineer", saved.JobTitle);
        // What it must not have disturbed:
        Assert.Equal(25, saved.AnnualLeaveEntitlement);
        Assert.Equal(18, saved.LeaveBalance);
    }

    /// <summary>
    /// The request type carries no leave fields at all, so there is nothing to send
    /// and no default-0 to arrive. A compile-time guarantee is worth pinning: this
    /// fails to build, not merely to assert, if either property comes back.
    /// </summary>
    [Fact]
    public void The_edit_request_exposes_no_leave_numbers()
    {
        var properties = typeof(EditEmployeeProfileRequest)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(nameof(EmployeeProfile.AnnualLeaveEntitlement), properties);
        Assert.DoesNotContain(nameof(EmployeeProfile.LeaveBalance), properties);
    }
}
