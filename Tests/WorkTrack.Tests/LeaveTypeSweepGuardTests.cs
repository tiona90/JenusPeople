using Application.Core;
using Application.LeaveTypes.Commands;
using Application.LeaveTypes.DTOs;
using AutoMapper;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Switching a type's approval off approves what is in flight, and every row it
/// approves has to clear what an ordinary approval clears. The sweep used to check
/// each row against the database alone, where none of the rows it had approved a
/// moment earlier were saved yet — so one employee's two pending requests each
/// passed on their own and together overspent the balance. It also skipped the
/// per-child ledger and the attachment policy, which CLAUDE.md promises run on
/// every path into Approved, and wrote the sweep and the balances in two
/// unprotected saves.
/// </summary>
public class LeaveTypeSweepGuardTests
{
    private const int AnnualTypeId = 1;
    private const int PaternityTypeId = 2;
    private const string UserId = "u-emp";
    private const string ProfileId = "p-emp";

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static async Task SeedAsync(AppDbContext db, int entitlement)
    {
        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });
        db.Departments.Add(new Department { Id = 1, Name = "Engineering", Code = "ENG" });
        db.Users.Add(new User { Id = UserId, UserName = UserId, Email = "emp@t.local", DisplayName = "Emp" });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ProfileId, UserId = UserId, DepartmentId = 1,
            AnnualLeaveEntitlement = entitlement, LeaveBalance = entitlement,
        });
        db.LeaveTypes.Add(new LeaveType
        {
            Id = AnnualTypeId, Name = "Annual Leave", IsActive = true, AffectsBalance = true,
            DefaultAllowance = entitlement, RequiresManagerApproval = true,
        });
        db.LeaveTypes.Add(new LeaveType
        {
            Id = PaternityTypeId, Name = "Paternity Leave", IsActive = true, RequiresManagerApproval = true,
            PerChildEntitlement = true, PerChildTotalWeeks = 18, PerChildWeeksPerYear = 1, ChildEligibleUntilAge = 15,
        });
        db.Children.Add(new Child { Id = "c1", EmployeeProfileId = ProfileId, Name = "Nico", DateOfBirth = new DateOnly(2024, 1, 1) });
        await db.SaveChangesAsync();
    }

    private static void AddPending(AppDbContext db, string id, int typeId, DateTime start, DateTime end, string? childId = null) =>
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = id, EmployeeId = UserId, EmployeeProfileId = ProfileId, DepartmentId = 1, LeaveTypeId = typeId,
            StartDate = start, EndDate = end, ChildId = childId, Reason = "Trip",
            Status = AnnualLeaveStatus.Pending, CreatedAt = DateTime.UtcNow,
        });

    private static UpsertLeaveTypeRequest AnnualOff(AttachmentPolicy policy = AttachmentPolicy.None, int allowance = 6) => new()
    {
        Name = "Annual Leave", IsActive = true, AffectsBalance = true, DefaultAllowance = allowance,
        RequiresManagerApproval = false, RequiresHrApproval = false, AttachmentPolicy = policy,
    };

    private static Task<Result<LeaveTypeDto>> UpdateAsync(AppDbContext db, int id, UpsertLeaveTypeRequest request) =>
        new UpdateLeaveType.Handler(db, Mapper()).Handle(new UpdateLeaveType.Command { Id = id, LeaveType = request }, CancellationToken.None);

    private static async Task<AnnualLeaveStatus[]> StatusesAsync(AppDbContext db) =>
        await db.AnnualLeaves.AsNoTracking().OrderBy(l => l.Id).Select(l => l.Status).ToArrayAsync();

    // Two Mon–Thu requests: four business days each.
    private static readonly DateTime WeekA = new(2024, 3, 4);
    private static readonly DateTime WeekB = new(2024, 3, 11);

    [Fact]
    public async Task Two_requests_that_together_overspend_the_balance_are_refused()
    {
        using var db = TestDb.Create();
        await SeedAsync(db, entitlement: 6);
        AddPending(db, "L1", AnnualTypeId, WeekA, WeekA.AddDays(3));
        AddPending(db, "L2", AnnualTypeId, WeekB, WeekB.AddDays(3));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await UpdateAsync(db, AnnualTypeId, AnnualOff());

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("Insufficient leave balance", result.Error);
        Assert.Equal([AnnualLeaveStatus.Pending, AnnualLeaveStatus.Pending], await StatusesAsync(db));
    }

    [Fact]
    public async Task Two_requests_that_fit_the_balance_are_both_approved()
    {
        using var db = TestDb.Create();
        await SeedAsync(db, entitlement: 10);
        AddPending(db, "L1", AnnualTypeId, WeekA, WeekA.AddDays(3));
        AddPending(db, "L2", AnnualTypeId, WeekB, WeekB.AddDays(3));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await UpdateAsync(db, AnnualTypeId, AnnualOff(allowance: 10));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal([AnnualLeaveStatus.Approved, AnnualLeaveStatus.Approved], await StatusesAsync(db));
    }

    [Fact]
    public async Task An_undocumented_request_on_a_type_requiring_a_document_is_not_swept_into_approved()
    {
        using var db = TestDb.Create();
        await SeedAsync(db, entitlement: 20);
        AddPending(db, "L1", AnnualTypeId, WeekA, WeekA.AddDays(3));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await UpdateAsync(db, AnnualTypeId, AnnualOff(AttachmentPolicy.Required, allowance: 20));

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Equal([AnnualLeaveStatus.Pending], await StatusesAsync(db));
    }

    [Fact]
    public async Task Two_requests_for_one_child_that_together_break_the_yearly_cap_are_refused()
    {
        using var db = TestDb.Create();
        await SeedAsync(db, entitlement: 20);
        // Three business days each against a one-week (five-day) yearly cap.
        AddPending(db, "L1", PaternityTypeId, WeekA, WeekA.AddDays(2), childId: "c1");
        AddPending(db, "L2", PaternityTypeId, WeekB, WeekB.AddDays(2), childId: "c1");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await UpdateAsync(db, PaternityTypeId, new UpsertLeaveTypeRequest
        {
            Name = "Paternity Leave", IsActive = true, RequiresManagerApproval = false, RequiresHrApproval = false,
            PerChildEntitlement = true, PerChildTotalWeeks = 18, PerChildWeeksPerYear = 1, ChildEligibleUntilAge = 15,
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("Nico", result.Error);
        Assert.Equal([AnnualLeaveStatus.Pending, AnnualLeaveStatus.Pending], await StatusesAsync(db));
    }

    [Fact]
    public async Task A_failed_balance_write_rolls_back_the_sweep()
    {
        var interceptor = new FailOnNthSaveInterceptor(failOnSave: 2);
        await using var db = await TransactionalTestDb.CreateAsync(interceptor);
        await SeedAsync(db, entitlement: 10);
        AddPending(db, "L1", AnnualTypeId, WeekA, WeekA.AddDays(3));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        interceptor.Arm();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => UpdateAsync(db, AnnualTypeId, AnnualOff(allowance: 10)));

        Assert.Equal(FailOnNthSaveInterceptor.FailureMessage, thrown.Message);
        Assert.Equal(2, interceptor.SaveCount);
        db.ChangeTracker.Clear();
        Assert.Equal([AnnualLeaveStatus.Pending], await StatusesAsync(db));
        Assert.True((await db.LeaveTypes.AsNoTracking().SingleAsync(t => t.Id == AnnualTypeId)).RequiresManagerApproval);
    }
}
