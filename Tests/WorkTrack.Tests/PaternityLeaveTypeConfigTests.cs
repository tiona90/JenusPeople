using Application.LeaveTypes.DTOs;
using Application.LeaveTypes.Validators;
using Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The three numbers describing a per-child entitlement live on the leave type,
/// beside the allowance -- the same reasoning that moved MaxCarryoverDays there.
///
/// Two rules are worth stating out loud. A 0 here is refused, but unlike
/// EmployeeProfile.AnnualLeaveEntitlement (where a stored 0 disables the balance
/// check outright) a 0 that slipped through would refuse every request rather than
/// permit every request. And a per-child type must not also affect the pooled
/// balance: it keeps its own ledger, and being counted in both would charge one
/// day of leave twice.
/// </summary>
public class PaternityLeaveTypeConfigTests : IDisposable
{
    private readonly ServiceProvider _services;

    public PaternityLeaveTypeConfigTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"paternity-leave-type-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        collection.AddIdentityCore<User>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>();

        _services = collection.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();
    private UserManager<User> Users => _services.GetRequiredService<UserManager<User>>();
    private RoleManager<Role> Roles => _services.GetRequiredService<RoleManager<Role>>();

    /// <summary>
    /// A fresh database and a migrated one must agree. The migration corrects the
    /// deployed row; this holds the seeder's copy of the same figures, so a fresh
    /// clone does not come up with the old flat 14 days.
    /// </summary>
    [Fact]
    public async Task The_seeded_paternity_type_carries_the_per_child_policy()
    {
        await DbInitializer.SeedData(Db, Users, Roles, SeedPolicy.For("Development", demoData: false, allowInProduction: false));

        var paternity = await Db.LeaveTypes.SingleAsync(lt => lt.Name == "Paternity Leave");

        Assert.True(paternity.PerChildEntitlement);
        Assert.Equal(18, paternity.PerChildTotalWeeks);
        Assert.Equal(5, paternity.PerChildWeeksPerYear);
        Assert.Equal(15, paternity.ChildEligibleUntilAge);
        Assert.Equal("weeks/child", paternity.AllowanceUnit);
        // 0 so nothing quotes the dead flat allowance; the per-child figures are
        // the only ones that describe this type now.
        Assert.Equal(0, paternity.DefaultAllowance);
        // Display-only, but it contradicted the 5-week annual cap at 14.
        Assert.Equal(25, paternity.MaxConsecutiveDays);
        Assert.False(paternity.AffectsBalance);
    }

    private static UpsertLeaveTypeRequest Paternity(
        int totalWeeks = 18,
        int weeksPerYear = 5,
        int untilAge = 15,
        bool affectsBalance = false) => new()
    {
        Name = "Paternity Leave",
        RequiresApproval = true,
        IsActive = true,
        AffectsBalance = affectsBalance,
        PerChildEntitlement = true,
        PerChildTotalWeeks = totalWeeks,
        PerChildWeeksPerYear = weeksPerYear,
        ChildEligibleUntilAge = untilAge,
    };

    [Fact]
    public void The_configured_policy_is_valid()
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity());

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(261)]
    public void A_total_outside_the_allowed_range_is_rejected(int totalWeeks)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity(totalWeeks: totalWeeks));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.PerChildTotalWeeks));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(53)]
    public void A_yearly_cap_outside_the_allowed_range_is_rejected(int weeksPerYear)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity(weeksPerYear: weeksPerYear));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.PerChildWeeksPerYear));
    }

    [Fact]
    public void A_yearly_cap_above_the_total_is_rejected()
    {
        var result = new UpsertLeaveTypeRequestValidator()
            .Validate(Paternity(totalWeeks: 4, weeksPerYear: 5));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.PerChildWeeksPerYear));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void An_eligibility_age_outside_the_allowed_range_is_rejected(int untilAge)
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity(untilAge: untilAge));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.ChildEligibleUntilAge));
    }

    [Fact]
    public void A_per_child_type_may_not_also_affect_the_pooled_balance()
    {
        var result = new UpsertLeaveTypeRequestValidator().Validate(Paternity(affectsBalance: true));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertLeaveTypeRequest.AffectsBalance));
    }

    /// <summary>
    /// With the toggle off the three numbers describe nothing, so they must not be
    /// validated -- every existing leave type in the database has them at 0.
    /// </summary>
    [Fact]
    public void The_numbers_are_ignored_when_the_toggle_is_off()
    {
        var request = Paternity(totalWeeks: 0, weeksPerYear: 0, untilAge: 0);
        request.Name = "Annual Leave";
        request.PerChildEntitlement = false;
        request.AffectsBalance = true;
        request.DefaultAllowance = 25;

        var result = new UpsertLeaveTypeRequestValidator().Validate(request);

        Assert.True(result.IsValid);
    }
}
