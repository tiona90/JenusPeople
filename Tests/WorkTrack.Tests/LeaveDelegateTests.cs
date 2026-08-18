using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.AnnualLeaves.Validators;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Coverage (delegate) on a leave request: the colleague nominated to handle
/// urgent matters while the employee is away. It is optional, must point at a
/// real user, and can never be the requester themselves.
/// </summary>
public class LeaveDelegateTests
{
    private const string MissingDelegateMessage = "Selected delegate does not exist.";
    private const string SelfDelegateMessage = "You cannot nominate yourself to cover your own leave.";

    private static void SeedUsers(AppDbContext db)
    {
        db.Users.Add(new User { Id = "u1", UserName = "u1", Email = "u1@test.local", DisplayName = "Ada Lovelace" });
        db.Users.Add(new User { Id = "u2", UserName = "u2", Email = "u2@test.local", DisplayName = "Grace Hopper" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = 1 });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p2", UserId = "u2", DepartmentId = 1 });
    }

    private static CreateAnnualLeave.Command CreateCmd(string? delegateId, string emp = "u1") => new()
    {
        AnnualLeave = new CreateAnnualLeaveRequest
        {
            EmployeeId = emp,
            LeaveTypeId = 1,
            StartDate = DateTime.Parse("2024-03-04"),
            EndDate = DateTime.Parse("2024-03-08"),
            Reason = "test",
            DelegateId = delegateId,
        },
    };

    private static async Task<List<string>> ValidateCreate(AppDbContext db, CreateAnnualLeave.Command cmd)
    {
        var result = await new CreateAnnualLeaveRequestValidator(db).ValidateAsync(cmd);
        return result.Errors.Select(e => e.ErrorMessage).ToList();
    }

    [Fact]
    public async Task Create_without_a_delegate_raises_no_coverage_errors()
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        await db.SaveChangesAsync();

        var errors = await ValidateCreate(db, CreateCmd(delegateId: null));

        Assert.DoesNotContain(MissingDelegateMessage, errors);
        Assert.DoesNotContain(SelfDelegateMessage, errors);
    }

    [Fact]
    public async Task Create_with_a_blank_delegate_raises_no_coverage_errors()
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        await db.SaveChangesAsync();

        // The client sends "" when the picker was opened but nobody was chosen.
        var errors = await ValidateCreate(db, CreateCmd(delegateId: "   "));

        Assert.DoesNotContain(MissingDelegateMessage, errors);
        Assert.DoesNotContain(SelfDelegateMessage, errors);
    }

    [Fact]
    public async Task Create_with_a_real_colleague_as_delegate_is_accepted()
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        await db.SaveChangesAsync();

        var errors = await ValidateCreate(db, CreateCmd(delegateId: "u2"));

        Assert.DoesNotContain(MissingDelegateMessage, errors);
        Assert.DoesNotContain(SelfDelegateMessage, errors);
    }

    [Fact]
    public async Task Create_with_an_unknown_delegate_is_rejected()
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        await db.SaveChangesAsync();

        var errors = await ValidateCreate(db, CreateCmd(delegateId: "ghost"));

        Assert.Contains(MissingDelegateMessage, errors);
    }

    [Fact]
    public async Task Create_nominating_yourself_is_rejected()
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        await db.SaveChangesAsync();

        var errors = await ValidateCreate(db, CreateCmd(delegateId: "u1", emp: "u1"));

        Assert.Contains(SelfDelegateMessage, errors);
    }

    [Fact]
    public async Task Edit_nominating_the_leave_owner_is_rejected()
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L1",
            EmployeeId = "u1",
            EmployeeProfileId = "p1",
            LeaveTypeId = 1,
            Status = AnnualLeaveStatus.Pending,
            StartDate = DateTime.Parse("2024-03-04"),
            EndDate = DateTime.Parse("2024-03-08"),
        });
        await db.SaveChangesAsync();

        var result = await new EditAnnualLeaveRequestValidator(db).ValidateAsync(new EditAnnualLeave.Command
        {
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L1",
                LeaveTypeId = 1,
                StartDate = DateTime.Parse("2024-03-04"),
                EndDate = DateTime.Parse("2024-03-08"),
                Reason = "test",
                DelegateId = "u1",
            },
            ChangedByUserId = "u1",
        });

        Assert.Contains(SelfDelegateMessage, result.Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public async Task Edit_persists_a_nominated_delegate_and_clears_a_blank_one()
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L1",
            EmployeeId = "u1",
            EmployeeProfileId = "p1",
            LeaveTypeId = 1,
            Status = AnnualLeaveStatus.Pending,
            StartDate = DateTime.Parse("2024-03-04"),
            EndDate = DateTime.Parse("2024-03-08"),
        });
        await db.SaveChangesAsync();

        var handler = new EditAnnualLeave.Handler(db);

        EditAnnualLeave.Command EditCmd(string? delegateId) => new()
        {
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L1",
                LeaveTypeId = 1,
                StartDate = DateTime.Parse("2024-03-04"),
                EndDate = DateTime.Parse("2024-03-08"),
                Reason = "test",
                DelegateId = delegateId,
            },
            ChangedByUserId = "u1",
        };

        var set = await handler.Handle(EditCmd("u2"), CancellationToken.None);
        Assert.True(set.IsSuccess);
        Assert.Equal("u2", db.AnnualLeaves.Single(al => al.Id == "L1").DelegateId);

        // An empty delegate means "no cover", not "keep whatever was there".
        var cleared = await handler.Handle(EditCmd("  "), CancellationToken.None);
        Assert.True(cleared.IsSuccess);
        Assert.Null(db.AnnualLeaves.Single(al => al.Id == "L1").DelegateId);
    }
}
