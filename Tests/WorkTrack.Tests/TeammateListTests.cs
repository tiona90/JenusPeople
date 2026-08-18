using Application.EmployeeProfiles.Queries;
using Domain;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The teammate list backs the leave-coverage picker, so every authenticated
/// user can read it. It must therefore stay tightly scoped: own department only,
/// never yourself, and never an admin.
/// </summary>
public class TeammateListTests
{
    private const string MeUserId = "me";

    private static void SeedProfile(AppDbContext db, string userId, int departmentId, string displayName)
    {
        db.Users.Add(new User { Id = userId, UserName = userId, Email = $"{userId}@test.local", DisplayName = displayName });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = $"p-{userId}",
            UserId = userId,
            DepartmentId = departmentId,
            JobTitle = "Engineer",
        });
    }

    private static Task<List<Application.EmployeeProfiles.DTOs.TeammateDto>> Run(AppDbContext db, string userId = MeUserId) =>
        new GetTeammateList.Handler(db).Handle(new GetTeammateList.Query { RequestingUserId = userId }, CancellationToken.None);

    [Fact]
    public async Task Returns_department_colleagues_without_the_caller()
    {
        using var db = TestDb.Create();
        SeedProfile(db, MeUserId, 1, "Me Myself");
        SeedProfile(db, "mate", 1, "Grace Hopper");
        await db.SaveChangesAsync();

        var teammates = await Run(db);

        Assert.Equal(["Grace Hopper"], teammates.Select(t => t.DisplayName));
    }

    [Fact]
    public async Task Excludes_other_departments()
    {
        using var db = TestDb.Create();
        SeedProfile(db, MeUserId, 1, "Me Myself");
        SeedProfile(db, "mate", 1, "Grace Hopper");
        SeedProfile(db, "outsider", 2, "Alan Turing");
        await db.SaveChangesAsync();

        var teammates = await Run(db);

        Assert.Equal(["Grace Hopper"], teammates.Select(t => t.DisplayName));
    }

    [Fact]
    public async Task Excludes_admins()
    {
        using var db = TestDb.Create();
        SeedProfile(db, MeUserId, 1, "Me Myself");
        SeedProfile(db, "boss", 1, "Big Boss");

        var adminRole = new Role { Id = "r-admin", Name = AppRoles.Admin, NormalizedName = AppRoles.Admin.ToUpperInvariant() };
        db.Roles.Add(adminRole);
        db.UserRoles.Add(new UserRole { UserId = "boss", RoleId = adminRole.Id });
        await db.SaveChangesAsync();

        var teammates = await Run(db);

        Assert.Empty(teammates);
    }

    [Fact]
    public async Task Returns_nothing_when_the_caller_has_no_profile()
    {
        using var db = TestDb.Create();
        SeedProfile(db, "mate", 1, "Grace Hopper");
        await db.SaveChangesAsync();

        Assert.Empty(await Run(db, "stranger"));
    }
}
