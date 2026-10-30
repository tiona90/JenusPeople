using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.AnnualLeaves.Validators;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Coverage (delegate) on a leave request: the colleague nominated to handle
/// urgent matters while the employee is away. It must point at a real user and
/// can never be the requester themselves. Whether it may be left blank at all is
/// <c>CoverageRule</c>'s question, covered in <see cref="CoverageRequiredTests"/>;
/// the users seeded here hold no role, so the rule reads them as non-System Administrators and
/// asks for one — these tests look only at the two shape checks.
/// </summary>
public class LeaveDelegateTests
{
    private const string MissingDelegateMessage = CoverageRule.DelegateNotEligibleMessage;
    private const string SelfDelegateMessage = "You cannot nominate yourself to cover your own leave.";

    private static void SeedUsers(AppDbContext db)
    {
        db.Users.Add(new User { Id = "u1", UserName = "u1", Email = "u1@test.local", DisplayName = "Ada Lovelace" });
        db.Users.Add(new User { Id = "u2", UserName = "u2", Email = "u2@test.local", DisplayName = "Grace Hopper" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = 1 });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p2", UserId = "u2", DepartmentId = 1 });
        db.Users.Add(new User { Id = "u3", UserName = "u3", Email = "u3@test.local", DisplayName = "Edsger Dijkstra" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p3", UserId = "u3", DepartmentId = 2 });
        db.Users.Add(new User { Id = "u4", UserName = "u4", Email = "u4@test.local", DisplayName = "Left Already", IsActive = false });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p4", UserId = "u4", DepartmentId = 1 });
        db.Roles.Add(new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() });
        db.Users.Add(new User { Id = "u5", UserName = "u5", Email = "u5@test.local", DisplayName = "Helen HR" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p5", UserId = "u5", DepartmentId = 1 });
        db.UserRoles.Add(new UserRole { UserId = "u5", RoleId = "r-hr" });
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

        var handler = new EditAnnualLeave.Handler(db, new FakeEmailService());

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

        // An empty delegate means "no cover", not "keep whatever was there". The
        // validator refuses this for an Employee (CoverageRequiredTests); the
        // handler itself, reached here directly, still stores what it is given.
        var cleared = await handler.Handle(EditCmd("  "), CancellationToken.None);
        Assert.True(cleared.IsSuccess);
        Assert.Null(db.AnnualLeaves.Single(al => al.Id == "L1").DelegateId);
    }

    // ── Who may cover: the colleagues the picker offers, and nobody else ─────────
    // The delegate is told about the absence and handed the note and the handover
    // file, so a delegate outside the department, an administrator or a leaver was
    // a way to send them to anyone. The picker (GetTeammateList) only ever offered
    // active, non-administrator colleagues in the employee's department.

    [Theory]
    [InlineData("u3")] // another department
    [InlineData("u4")] // deactivated
    [InlineData("u5")] // an HR Administrator
    public async Task Create_with_a_delegate_the_picker_would_not_offer_is_rejected(string delegateId)
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        await db.SaveChangesAsync();

        var errors = await ValidateCreate(db, CreateCmd(delegateId));

        Assert.Contains(MissingDelegateMessage, errors);
    }

    private static async Task<List<string>> ValidateEdit(AppDbContext db, string? storedDelegate, string newDelegate)
    {
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L1", EmployeeId = "u1", EmployeeProfileId = "p1", LeaveTypeId = 1, DelegateId = storedDelegate,
            Status = AnnualLeaveStatus.Pending, StartDate = DateTime.Parse("2024-03-04"), EndDate = DateTime.Parse("2024-03-08"),
        });
        await db.SaveChangesAsync();

        var result = await new EditAnnualLeaveRequestValidator(db).ValidateAsync(new EditAnnualLeave.Command
        {
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = "L1", LeaveTypeId = 1, Reason = "test", DelegateId = newDelegate,
                StartDate = DateTime.Parse("2024-03-04"), EndDate = DateTime.Parse("2024-03-08"),
            },
            ChangedByUserId = "u1",
        });
        return result.Errors.Select(e => e.ErrorMessage).ToList();
    }

    [Fact]
    public async Task Edit_switching_to_a_delegate_in_another_department_is_rejected()
    {
        using var db = TestDb.Create();
        SeedUsers(db);

        Assert.Contains(MissingDelegateMessage, await ValidateEdit(db, storedDelegate: "u2", newDelegate: "u3"));
    }

    /// <summary>
    /// The edit dialog sends the delegate back whether or not anyone touched it, so
    /// a delegate who has since moved department must not stop a typo fix.
    /// </summary>
    [Fact]
    public async Task Edit_keeping_the_stored_delegate_is_accepted_even_if_they_have_moved()
    {
        using var db = TestDb.Create();
        SeedUsers(db);

        Assert.DoesNotContain(MissingDelegateMessage, await ValidateEdit(db, storedDelegate: "u3", newDelegate: "u3"));
    }

    [Fact]
    public async Task The_picker_does_not_offer_a_deactivated_colleague()
    {
        using var db = TestDb.Create();
        SeedUsers(db);
        await db.SaveChangesAsync();

        var teammates = await new Application.EmployeeProfiles.Queries.GetTeammateList.Handler(db)
            .Handle(new Application.EmployeeProfiles.Queries.GetTeammateList.Query { RequestingUserId = "u1" }, CancellationToken.None);

        Assert.Contains(teammates, t => t.UserId == "u2");
        Assert.DoesNotContain(teammates, t => t.UserId == "u4");
    }
}
