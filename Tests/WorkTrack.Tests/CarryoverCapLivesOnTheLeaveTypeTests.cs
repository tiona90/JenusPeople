using Application.Core;
using Application.LeaveTypes.Commands;
using Application.LeaveTypes.DTOs;
using Application.LeaveTypes.Validators;
using Application.Settings.Commands;
using AutoMapper;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The year-end carryover cap was an org-wide number on <c>AppSettings</c>, edited on
/// Leave Settings, while the allowance it bounds is a column on the leave type. One
/// figure capping another it could not see, a screen away — and no way to say that
/// sick leave carries nothing while annual leave carries five, because there was only
/// ever one cap for the whole company.
///
/// It lives on <see cref="LeaveType"/> now, beside <c>DefaultAllowance</c>, the same
/// move <c>RemoveAppSettingsDefaultAnnualEntitlement</c> made for the allowance. These
/// tests hold the cap on the leave type and off app settings.
/// </summary>
public class CarryoverCapLivesOnTheLeaveTypeTests
{
    private static IMapper BuildMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfiles>(),
            NullLoggerFactory.Instance).CreateMapper();

    private static Task<Result<LeaveTypeDto>> Handle(AppDbContext db, UpdateLeaveType.Command command) =>
        new UpdateLeaveType.Handler(db, BuildMapper()).Handle(command, CancellationToken.None);

    private static UpsertLeaveTypeRequest Request(string name, bool affectsBalance, int allowance, int carryover) => new()
    {
        Name = name,
        RequiresApproval = true,
        IsActive = true,
        AffectsBalance = affectsBalance,
        DefaultAllowance = allowance,
        MaxCarryoverDays = carryover,
    };

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public void A_cap_outside_a_year_is_rejected(int days)
    {
        var result = new UpsertLeaveTypeRequestValidator()
            .Validate(Request("Annual Leave", affectsBalance: true, allowance: 25, carryover: days));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.MaxCarryoverDays));
    }

    /// <summary>
    /// Unlike the allowance, a 0 here is an ordinary policy — nothing carries over —
    /// not a switch that disables a check. Only <c>DefaultAllowance</c> has that
    /// hazard, and <c>UpdateLeaveType</c> guards it separately.
    /// </summary>
    [Fact]
    public void Carrying_nothing_over_is_a_policy_not_an_error()
    {
        var result = new UpsertLeaveTypeRequestValidator()
            .Validate(Request("Sick Leave", affectsBalance: false, allowance: 10, carryover: 0));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Updating_a_leave_type_stores_its_cap()
    {
        using var db = TestDb.Create();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = 1, Name = "Annual Leave", IsActive = true,
            RequiresApproval = true, AffectsBalance = true, DefaultAllowance = 25, MaxCarryoverDays = 5,
        });
        await db.SaveChangesAsync();

        var result = await Handle(db, new UpdateLeaveType.Command
        {
            Id = 1,
            LeaveType = Request("Annual Leave", affectsBalance: true, allowance: 25, carryover: 8),
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(8, result.Value!.MaxCarryoverDays);
        Assert.Equal(8, (await db.LeaveTypes.AsNoTracking().SingleAsync(t => t.Id == 1)).MaxCarryoverDays);
    }

    /// <summary>
    /// The thing one org-wide number could not express: two types, two caps, at once.
    /// </summary>
    [Fact]
    public async Task Each_leave_type_carries_its_own_cap()
    {
        using var db = TestDb.Create();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = 1, Name = "Annual Leave", IsActive = true,
            RequiresApproval = true, AffectsBalance = true, DefaultAllowance = 25,
        });
        db.LeaveTypes.Add(new LeaveType
        {
            Id = 2, Name = "Sick Leave", IsActive = true,
            RequiresApproval = true, AffectsBalance = false, DefaultAllowance = 10,
        });
        await db.SaveChangesAsync();

        await Handle(db, new UpdateLeaveType.Command
        {
            Id = 1,
            LeaveType = Request("Annual Leave", affectsBalance: true, allowance: 25, carryover: 5),
        });
        await Handle(db, new UpdateLeaveType.Command
        {
            Id = 2,
            LeaveType = Request("Sick Leave", affectsBalance: false, allowance: 10, carryover: 0),
        });

        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.MaxCarryoverDays);
        Assert.Equal(5, types[1]);
        Assert.Equal(0, types[2]);
    }

    /// <summary>
    /// The old column is gone rather than merely unused: leaving it would let a second
    /// cap be stored and quietly disagree with the leave type's, which is the drift
    /// this move exists to end.
    /// </summary>
    [Fact]
    public void App_settings_no_longer_carry_a_cap_of_their_own()
    {
        Assert.Null(typeof(AppSettings).GetProperty("MaxCarryoverDays"));
        Assert.Null(typeof(UpdateAppSettings.Command).GetProperty("MaxCarryoverDays"));
    }
}
