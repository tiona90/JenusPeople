using Application.Core;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests.Application;

public class ManagerAccessScopeResolverTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static EmployeeProfile Profile(
        string userId,
        int departmentId,
        string? managerId = null,
        string? id = null) => new()
        {
            Id = id ?? Guid.NewGuid().ToString(),
            UserId = userId,
            DepartmentId = departmentId,
            ManagerId = managerId,
        };

    // ── ResolveAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_Returns_Empty_Scope_When_Manager_Has_No_Profile()
    {
        using var ctx = CreateContext();
        await ctx.SaveChangesAsync();

        var scope = await ManagerAccessScopeResolver.ResolveAsync(
            ctx, "ghost-manager", CancellationToken.None);

        Assert.Empty(scope.ManagedDepartmentIds);
        Assert.Empty(scope.ManagerProfileIds);
        Assert.Empty(scope.DirectReportUserIds);
    }

    [Fact]
    public async Task Resolve_Returns_Manager_Own_Department()
    {
        using var ctx = CreateContext();
        ctx.EmployeeProfiles.Add(Profile(userId: "manager-1", departmentId: 10, id: "mgr-profile"));
        await ctx.SaveChangesAsync();

        var scope = await ManagerAccessScopeResolver.ResolveAsync(
            ctx, "manager-1", CancellationToken.None);

        Assert.Equal(new[] { 10 }, scope.ManagedDepartmentIds);
        Assert.Equal(new[] { "mgr-profile" }, scope.ManagerProfileIds);
    }

    [Fact]
    public async Task Resolve_Returns_Direct_Reports_Linked_To_Manager_Profile()
    {
        using var ctx = CreateContext();
        ctx.EmployeeProfiles.AddRange(
            Profile(userId: "manager-1", departmentId: 10, id: "mgr-profile"),
            Profile(userId: "report-a", departmentId: 10, managerId: "mgr-profile"),
            Profile(userId: "report-b", departmentId: 10, managerId: "mgr-profile"),
            Profile(userId: "unrelated", departmentId: 20)); // no ManagerId — not a report
        await ctx.SaveChangesAsync();

        var scope = await ManagerAccessScopeResolver.ResolveAsync(
            ctx, "manager-1", CancellationToken.None);

        Assert.Contains("report-a", scope.DirectReportUserIds);
        Assert.Contains("report-b", scope.DirectReportUserIds);
        Assert.DoesNotContain("unrelated", scope.DirectReportUserIds);
    }

    [Fact]
    public async Task Resolve_Returns_All_Departments_When_Manager_Has_Multiple_Profiles()
    {
        // Some orgs let a manager hold multiple EmployeeProfiles across departments
        // (e.g. dotted-line reporting). Each profile contributes a department.
        using var ctx = CreateContext();
        ctx.EmployeeProfiles.AddRange(
            Profile(userId: "manager-1", departmentId: 10, id: "mgr-eng"),
            Profile(userId: "manager-1", departmentId: 20, id: "mgr-sales"));
        await ctx.SaveChangesAsync();

        var scope = await ManagerAccessScopeResolver.ResolveAsync(
            ctx, "manager-1", CancellationToken.None);

        Assert.Equal(new[] { 10, 20 }.OrderBy(x => x), scope.ManagedDepartmentIds.OrderBy(x => x));
        Assert.Equal(2, scope.ManagerProfileIds.Count);
    }

    [Fact]
    public async Task Resolve_DoesNot_Include_Reports_Of_Other_Managers()
    {
        // Manager A and Manager B are in the same department but have separate
        // direct reports. Resolving for A must not pull in B's reports.
        using var ctx = CreateContext();
        ctx.EmployeeProfiles.AddRange(
            Profile(userId: "manager-a", departmentId: 10, id: "mgr-a"),
            Profile(userId: "manager-b", departmentId: 10, id: "mgr-b"),
            Profile(userId: "report-of-a", departmentId: 10, managerId: "mgr-a"),
            Profile(userId: "report-of-b", departmentId: 10, managerId: "mgr-b"));
        await ctx.SaveChangesAsync();

        var scope = await ManagerAccessScopeResolver.ResolveAsync(
            ctx, "manager-a", CancellationToken.None);

        Assert.Contains("report-of-a", scope.DirectReportUserIds);
        Assert.DoesNotContain("report-of-b", scope.DirectReportUserIds);
    }

    [Fact]
    public async Task Resolve_Soft_Deleted_Profiles_Are_Excluded_From_Reports()
    {
        // EmployeeProfile has a global query filter on IsDeleted. A soft-deleted
        // direct report must not appear in the scope.
        using var ctx = CreateContext();
        var deletedReport = Profile(userId: "report-gone", departmentId: 10, managerId: "mgr-profile");
        deletedReport.IsDeleted = true;
        ctx.EmployeeProfiles.AddRange(
            Profile(userId: "manager-1", departmentId: 10, id: "mgr-profile"),
            Profile(userId: "report-active", departmentId: 10, managerId: "mgr-profile"),
            deletedReport);
        await ctx.SaveChangesAsync();

        var scope = await ManagerAccessScopeResolver.ResolveAsync(
            ctx, "manager-1", CancellationToken.None);

        Assert.Contains("report-active", scope.DirectReportUserIds);
        Assert.DoesNotContain("report-gone", scope.DirectReportUserIds);
    }
}
