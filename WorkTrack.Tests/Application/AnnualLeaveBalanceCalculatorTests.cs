using System.Reflection;
using System.Runtime.ExceptionServices;
using Application.AnnualLeaves.Commands;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests.Application;

public class AnnualLeaveBalanceCalculatorTests
{
    // The calculator is `internal static` so we invoke it via reflection rather than
    // adding InternalsVisibleTo on the Application project.
    private static readonly Type CalculatorType = typeof(CreateAnnualLeave).Assembly
        .GetType("Application.AnnualLeaves.Commands.AnnualLeaveBalanceCalculator")
        ?? throw new InvalidOperationException("AnnualLeaveBalanceCalculator type not found.");

    private static readonly MethodInfo EnsureMethod =
        CalculatorType.GetMethod("EnsureSufficientBalanceAsync", BindingFlags.Public | BindingFlags.Static)!;

    private static readonly MethodInfo SyncMethod =
        CalculatorType.GetMethod("SyncCurrentYearBalanceAsync", BindingFlags.Public | BindingFlags.Static)!;

    private static async Task InvokeEnsureAsync(
        AppDbContext context, EmployeeProfile profile, AnnualLeave leave, string? excludeId)
    {
        try
        {
            var task = (Task)EnsureMethod.Invoke(null,
                new object?[] { context, profile, leave, excludeId, CancellationToken.None })!;
            await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    private static async Task InvokeSyncAsync(AppDbContext context, EmployeeProfile profile)
    {
        var task = (Task)SyncMethod.Invoke(null,
            new object?[] { context, profile, CancellationToken.None })!;
        await task;
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static LeaveType BalanceAffectingType(int id = 1) => new()
    {
        Id = id,
        Name = "Annual",
        IsActive = true,
        AffectsBalance = true,
        RequiresApproval = true,
    };

    private static LeaveType NonBalanceType(int id = 2) => new()
    {
        Id = id,
        Name = "Sick",
        IsActive = true,
        AffectsBalance = false,
        RequiresApproval = false,
    };

    private static EmployeeProfile Profile(string userId, int entitlement) => new()
    {
        Id = Guid.NewGuid().ToString(),
        UserId = userId,
        DepartmentId = 1,
        AnnualLeaveEntitlement = entitlement,
        LeaveBalance = entitlement,
    };

    private static AnnualLeave Leave(
        string employeeId,
        DateTime start,
        DateTime end,
        int? leaveTypeId = 1,
        AnnualLeaveStatus status = AnnualLeaveStatus.Pending,
        string? id = null) => new()
        {
            Id = id ?? Guid.NewGuid().ToString(),
            EmployeeId = employeeId,
            StartDate = start,
            EndDate = end,
            LeaveTypeId = leaveTypeId,
            Status = status,
            Reason = "test",
        };

    // ── EnsureSufficientBalanceAsync ────────────────────────────────────────

    [Fact]
    public async Task EnsureSufficient_ReturnsEarly_WhenLeaveTypeDoesNotAffectBalance()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(NonBalanceType(2));
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 1);
        // 30 business days requested but balance is only 1 — should not throw because type doesn't affect balance.
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 2, 13), leaveTypeId: 2);

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    [Fact]
    public async Task EnsureSufficient_ReturnsEarly_WhenEntitlementIsZero()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 0);
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 1, 9));

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    [Fact]
    public async Task EnsureSufficient_AllowsRequest_WhenBalanceIsSufficient()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 20);
        // Mon–Fri = 5 business days.
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 1, 9));

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    [Fact]
    public async Task EnsureSufficient_Throws_WhenRequestExceedsEntitlement()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 3);
        // 5 business days vs entitlement 3.
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 1, 9));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeEnsureAsync(ctx, profile, leave, excludeId: null));
        Assert.Contains("Insufficient leave balance", ex.Message);
    }

    [Fact]
    public async Task EnsureSufficient_SubtractsApprovedDaysFromBalance()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());

        // Previously approved 4 business days in 2026.
        ctx.AnnualLeaves.Add(Leave(
            "user-1",
            new DateTime(2026, 2, 2),
            new DateTime(2026, 2, 5),
            leaveTypeId: 1,
            status: AnnualLeaveStatus.Approved));

        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 5);
        // Request 2 more business days — should fit (5 − 4 = 1 remaining, requested 2 → throws).
        var leave = Leave("user-1", new DateTime(2026, 3, 2), new DateTime(2026, 3, 3));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeEnsureAsync(ctx, profile, leave, excludeId: null));
        Assert.Contains("Remaining balance: 1", ex.Message);
    }

    [Fact]
    public async Task EnsureSufficient_ExcludesSpecifiedLeaveId_FromUsedDays()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());

        const string editingLeaveId = "leave-being-edited";
        ctx.AnnualLeaves.Add(Leave(
            "user-1",
            new DateTime(2026, 2, 2),
            new DateTime(2026, 2, 6), // 5 business days
            leaveTypeId: 1,
            status: AnnualLeaveStatus.Approved,
            id: editingLeaveId));

        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 5);
        // Same window edited — should not double-count the prior record.
        var leave = Leave("user-1", new DateTime(2026, 2, 2), new DateTime(2026, 2, 6), id: editingLeaveId);

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: editingLeaveId);
    }

    [Fact]
    public async Task EnsureSufficient_SkipsWeekends_InBusinessDayCount()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 5);
        // Mon 2026-01-05 → Sun 2026-01-11 spans 7 calendar days but only 5 business days.
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 1, 11));

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    [Fact]
    public async Task EnsureSufficient_ExcludesPublicHolidays_WhenCountryConfigured()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        ctx.AppSettings.Add(new AppSettings
        {
            LeaveYearStartMonth = 1,
            HolidayCountryCode = "CY",
        });
        ctx.PublicHolidays.Add(new PublicHoliday
        {
            CountryCode = "CY",
            Year = 2026,
            Date = new DateTime(2026, 1, 6), // Tuesday — falls inside requested window
            LocalName = "Epiphany",
            EnglishName = "Epiphany",
        });
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 4);
        // Mon–Fri = 5 business days, but Tue is a holiday → 4 chargeable days, exactly the entitlement.
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 1, 9));

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    [Fact]
    public async Task EnsureSufficient_HonorsCustomLeaveYearStartMonth_ForApprovedDayLookup()
    {
        // Leave year starts in April. A leave taken in May 2025 belongs to leave-year-key 2025
        // (Apr 2025 – Mar 2026). A new leave request in February 2026 falls in the SAME leave year,
        // so the May usage must count against entitlement.
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        ctx.AppSettings.Add(new AppSettings { LeaveYearStartMonth = 4 });

        ctx.AnnualLeaves.Add(Leave(
            "user-1",
            new DateTime(2025, 5, 5),
            new DateTime(2025, 5, 9), // 5 business days
            leaveTypeId: 1,
            status: AnnualLeaveStatus.Approved));
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 6);
        // Request 2 business days in Feb 2026 — same leave year as May 2025.
        var leave = Leave("user-1", new DateTime(2026, 2, 2), new DateTime(2026, 2, 3));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeEnsureAsync(ctx, profile, leave, excludeId: null));
        Assert.Contains("Remaining balance: 1", ex.Message);
    }

    [Fact]
    public async Task EnsureSufficient_IgnoresOtherEmployeesApprovedLeaves()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());

        ctx.AnnualLeaves.Add(Leave(
            "OTHER-user",
            new DateTime(2026, 2, 2),
            new DateTime(2026, 2, 13), // 10 business days for someone else
            leaveTypeId: 1,
            status: AnnualLeaveStatus.Approved));
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 5);
        var leave = Leave("user-1", new DateTime(2026, 3, 2), new DateTime(2026, 3, 6)); // 5 days

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    [Fact]
    public async Task EnsureSufficient_IgnoresPendingLeaves_OnlyApprovedCount()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());

        ctx.AnnualLeaves.Add(Leave(
            "user-1",
            new DateTime(2026, 2, 2),
            new DateTime(2026, 2, 13), // 10 days but only PENDING
            leaveTypeId: 1,
            status: AnnualLeaveStatus.Pending));
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 5);
        var leave = Leave("user-1", new DateTime(2026, 3, 2), new DateTime(2026, 3, 6));

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    [Fact]
    public async Task EnsureSufficient_HolidayOnWeekend_IsNotDoubleSkipped()
    {
        // A holiday that falls on a Saturday is already excluded by the weekend filter.
        // It must not be deducted a second time, otherwise the business-day count is wrong.
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        ctx.AppSettings.Add(new AppSettings { LeaveYearStartMonth = 1, HolidayCountryCode = "CY" });
        ctx.PublicHolidays.Add(new PublicHoliday
        {
            CountryCode = "CY",
            Year = 2026,
            Date = new DateTime(2026, 1, 10), // Saturday
            LocalName = "Test",
            EnglishName = "Test",
        });
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 10);
        // Mon 2026-01-05 → Fri 2026-01-16 spans two weeks = 10 business days.
        // The in-range Saturday holiday must NOT reduce the count further.
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 1, 16));

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    [Fact]
    public async Task EnsureSufficient_HolidayForDifferentCountry_IsIgnored()
    {
        // A holiday row for a country other than the configured one must not be applied.
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        ctx.AppSettings.Add(new AppSettings { LeaveYearStartMonth = 1, HolidayCountryCode = "GB" });
        ctx.PublicHolidays.Add(new PublicHoliday
        {
            CountryCode = "CY", // wrong country
            Year = 2026,
            Date = new DateTime(2026, 1, 6),
            LocalName = "Epiphany",
            EnglishName = "Epiphany",
        });
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 4);
        // 5 business days requested but entitlement is 4. The CY holiday is irrelevant
        // for a GB tenant, so the count stays at 5 and the call must throw.
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 1, 9));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeEnsureAsync(ctx, profile, leave, excludeId: null));
        Assert.Contains("Insufficient leave balance", ex.Message);
    }

    [Fact]
    public async Task EnsureSufficient_NoCountryCodeConfigured_HolidayRowsAreIgnored()
    {
        // When HolidayCountryCode is null/empty, GetHolidaySetAsync short-circuits to an
        // empty set even if PublicHolidays rows exist — public holidays are opt-in.
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        ctx.AppSettings.Add(new AppSettings { LeaveYearStartMonth = 1, HolidayCountryCode = null });
        ctx.PublicHolidays.Add(new PublicHoliday
        {
            CountryCode = "CY",
            Year = 2026,
            Date = new DateTime(2026, 1, 6),
            LocalName = "Epiphany",
            EnglishName = "Epiphany",
        });
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 4);
        // 5 business days vs entitlement 4 — must throw because no holiday is applied.
        var leave = Leave("user-1", new DateTime(2026, 1, 5), new DateTime(2026, 1, 9));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeEnsureAsync(ctx, profile, leave, excludeId: null));
    }

    [Fact]
    public async Task EnsureSufficient_ApprovedUnpaidLeaves_DoNotConsumePaidBalance()
    {
        // Paid (AffectsBalance=true) vs unpaid (AffectsBalance=false) types must be
        // independent: prior approved UNPAID leave does not reduce the balance for a
        // new PAID request.
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType(1)); // paid
        ctx.LeaveTypes.Add(NonBalanceType(2));       // unpaid

        // 10 business days of UNPAID approved leave already on file.
        ctx.AnnualLeaves.Add(Leave(
            "user-1",
            new DateTime(2026, 2, 2),
            new DateTime(2026, 2, 13),
            leaveTypeId: 2,
            status: AnnualLeaveStatus.Approved));
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 5);
        // Request 5 PAID business days — should succeed because unpaid history is invisible
        // to the paid-balance check.
        var leave = Leave("user-1", new DateTime(2026, 3, 2), new DateTime(2026, 3, 6), leaveTypeId: 1);

        await InvokeEnsureAsync(ctx, profile, leave, excludeId: null);
    }

    // ── SyncCurrentYearBalanceAsync ─────────────────────────────────────────

    [Fact]
    public async Task SyncCurrentYearBalance_SetsBalanceToEntitlement_WhenNoApprovedLeaves()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 25);
        profile.LeaveBalance = 0; // start unsynced

        await InvokeSyncAsync(ctx, profile);

        Assert.Equal(25, profile.LeaveBalance);
    }

    [Fact]
    public async Task SyncCurrentYearBalance_SubtractsApprovedBusinessDays_InCurrentLeaveYear()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        ctx.AppSettings.Add(new AppSettings { LeaveYearStartMonth = 1 });

        // Approved leave inside current leave year — 3 business days.
        var today = DateTime.UtcNow.Date;
        // Walk back to most recent Monday to make the window deterministic.
        var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (monday > today) monday = monday.AddDays(-7);
        ctx.AnnualLeaves.Add(Leave(
            "user-1",
            monday,
            monday.AddDays(2), // Mon–Wed = 3 business days
            leaveTypeId: 1,
            status: AnnualLeaveStatus.Approved));
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 20);

        await InvokeSyncAsync(ctx, profile);

        Assert.Equal(17, profile.LeaveBalance);
    }

    [Fact]
    public async Task SyncCurrentYearBalance_FloorsAtZero_WhenUsedExceedsEntitlement()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(BalanceAffectingType());
        ctx.AppSettings.Add(new AppSettings { LeaveYearStartMonth = 1 });

        var today = DateTime.UtcNow.Date;
        var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (monday > today) monday = monday.AddDays(-7);
        // 10 business days approved.
        ctx.AnnualLeaves.Add(Leave(
            "user-1",
            monday,
            monday.AddDays(13),
            leaveTypeId: 1,
            status: AnnualLeaveStatus.Approved));
        await ctx.SaveChangesAsync();

        var profile = Profile("user-1", entitlement: 5);

        await InvokeSyncAsync(ctx, profile);

        Assert.Equal(0, profile.LeaveBalance);
    }
}
