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
/// Leave is configured once, for everyone: the allowance on the balance-affecting
/// leave type. There used to be three numbers that could disagree — an org-wide
/// app setting, this allowance, and a per-employee entitlement — and out of the box
/// two of them did (20 against 25).
///
/// So moving the allowance has to move every employee with it. Otherwise the screens
/// quote the new figure while <c>AnnualLeaveBalanceCalculator</c> still enforces each
/// profile's stale <c>AnnualLeaveEntitlement</c>, which is the same disagreement in a
/// new place.
/// </summary>
public class AllowanceGovernsEveryoneTests
{
    private const int AnnualLeaveTypeId = 1;
    private const int SickLeaveTypeId = 2;

    private static IMapper BuildMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfiles>(),
            NullLoggerFactory.Instance).CreateMapper();

    private static Task<Result<LeaveTypeDto>> Handle(AppDbContext db, UpdateLeaveType.Command command) =>
        new UpdateLeaveType.Handler(db, BuildMapper()).Handle(command, CancellationToken.None);

    private static void SeedTypes(AppDbContext db)
    {
        db.LeaveTypes.Add(new LeaveType
        {
            Id = AnnualLeaveTypeId, Name = "Annual Leave", IsActive = true,
            RequiresApproval = true, AffectsBalance = true, DefaultAllowance = 25,
        });
        // A separate budget with an allowance of its own; moving it must change nothing.
        db.LeaveTypes.Add(new LeaveType
        {
            Id = SickLeaveTypeId, Name = "Sick Leave", IsActive = true,
            RequiresApproval = true, AffectsBalance = false, DefaultAllowance = 10,
        });
    }

    /// <summary>
    /// A Monday well inside the leave year, so a five-day range lands Mon-Fri and
    /// covers exactly five business days whatever year the suite runs in.
    /// </summary>
    private static DateTime FirstMondayOfMarch(int year)
    {
        var date = new DateTime(year, 3, 1);
        while (date.DayOfWeek != DayOfWeek.Monday)
            date = date.AddDays(1);
        return date;
    }

    private static void SeedProfile(AppDbContext db, string userId, int entitlement, int balance)
    {
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = $"p-{userId}",
            UserId = userId,
            AnnualLeaveEntitlement = entitlement,
            LeaveBalance = balance,
        });
    }

    /// <summary>Moves the allowance without touching anything else about the type.</summary>
    private static UpdateLeaveType.Command SetAllowance(int leaveTypeId, string name, bool affectsBalance, int days) => new()
    {
        Id = leaveTypeId,
        LeaveType = new UpsertLeaveTypeRequest
        {
            Name = name,
            RequiresApproval = true,
            IsActive = true,
            AffectsBalance = affectsBalance,
            DefaultAllowance = days,
        },
    };

    [Fact]
    public async Task Raising_the_allowance_raises_every_employees_entitlement()
    {
        using var db = TestDb.Create();
        SeedTypes(db);
        SeedProfile(db, "u-1", entitlement: 25, balance: 25);
        SeedProfile(db, "u-2", entitlement: 25, balance: 25);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, SetAllowance(AnnualLeaveTypeId, "Annual Leave", affectsBalance: true, days: 30));

        Assert.True(result.IsSuccess, result.Error);
        var profiles = await db.EmployeeProfiles.AsNoTracking().ToListAsync();
        Assert.All(profiles, p => Assert.Equal(30, p.AnnualLeaveEntitlement));
    }

    /// <summary>
    /// The balance is entitlement minus days already taken, so it has to be recomputed
    /// rather than left at whatever the old entitlement implied.
    /// </summary>
    [Fact]
    public async Task The_remaining_balance_is_recomputed_from_the_new_allowance()
    {
        using var db = TestDb.Create();
        SeedTypes(db);
        SeedProfile(db, "u-1", entitlement: 25, balance: 20);
        // Five business days already approved and taken. The Monday is computed
        // rather than written down: a fixed date drifts across weekdays each year,
        // which silently changes how many business days the range covers.
        var monday = FirstMondayOfMarch(DateTime.UtcNow.Year);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = "L-1",
            EmployeeId = "u-1",
            EmployeeProfileId = "p-u-1",
            LeaveTypeId = AnnualLeaveTypeId,
            Status = AnnualLeaveStatus.Approved,
            Reason = "Taken",
            StartDate = monday,
            EndDate = monday.AddDays(4),
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, SetAllowance(AnnualLeaveTypeId, "Annual Leave", affectsBalance: true, days: 30));

        Assert.True(result.IsSuccess, result.Error);
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(30, profile.AnnualLeaveEntitlement);
        // 30 granted, 5 already taken.
        Assert.Equal(25, profile.LeaveBalance);
    }

    [Fact]
    public async Task Lowering_the_allowance_lowers_every_employees_entitlement()
    {
        using var db = TestDb.Create();
        SeedTypes(db);
        SeedProfile(db, "u-1", entitlement: 25, balance: 25);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, SetAllowance(AnnualLeaveTypeId, "Annual Leave", affectsBalance: true, days: 18));

        Assert.True(result.IsSuccess, result.Error);
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(18, profile.AnnualLeaveEntitlement);
    }

    /// <summary>
    /// Sick leave's allowance is a different budget. Editing it must not touch the
    /// annual-leave entitlement, which is the pool the API actually enforces.
    /// </summary>
    [Fact]
    public async Task Moving_a_non_balance_types_allowance_changes_no_entitlement()
    {
        using var db = TestDb.Create();
        SeedTypes(db);
        SeedProfile(db, "u-1", entitlement: 25, balance: 25);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, SetAllowance(SickLeaveTypeId, "Sick Leave", affectsBalance: false, days: 14));

        Assert.True(result.IsSuccess, result.Error);
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(25, profile.AnnualLeaveEntitlement);
    }

    /// <summary>
    /// A 0 allowance would stamp a 0 entitlement, and an entitlement of 0 switches the
    /// approval-time balance check off outright — every request would sail through
    /// unpoliced. Refuse the edit rather than silently unpolicing the whole company.
    /// </summary>
    [Fact]
    public async Task An_allowance_of_zero_is_refused_rather_than_unpolicing_everyone()
    {
        using var db = TestDb.Create();
        SeedTypes(db);
        SeedProfile(db, "u-1", entitlement: 25, balance: 25);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, SetAllowance(AnnualLeaveTypeId, "Annual Leave", affectsBalance: true, days: 0));

        Assert.False(result.IsSuccess);
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(25, profile.AnnualLeaveEntitlement);
    }

    /// <summary>
    /// An edit that leaves the allowance alone must not rewrite anything.
    ///
    /// This used to make its edit a rename, to "Holiday". Annual Leave is one of the
    /// built-in types now and cannot be renamed (<see cref="Domain.SystemLeaveTypes"/>),
    /// so the edit is instead the one the helper already makes incidentally: it sends
    /// no Description, clearing the seeded one. Either way the allowance is unmoved,
    /// which is the whole point — the sweep keys on <c>AffectsBalance</c> and a
    /// changed allowance, never on the name.
    /// </summary>
    [Fact]
    public async Task An_edit_that_leaves_the_allowance_alone_leaves_entitlements_alone()
    {
        using var db = TestDb.Create();
        SeedTypes(db);
        // Deliberately not 25: proves the handler skipped the sweep rather than
        // happening to write the same number back.
        SeedProfile(db, "u-1", entitlement: 12, balance: 12);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Handle(db, SetAllowance(AnnualLeaveTypeId, "Annual Leave", affectsBalance: true, days: 25));

        Assert.True(result.IsSuccess, result.Error);
        var profile = await db.EmployeeProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(12, profile.AnnualLeaveEntitlement);
    }
}
