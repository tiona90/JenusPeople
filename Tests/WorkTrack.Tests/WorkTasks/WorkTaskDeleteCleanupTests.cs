using Application.AdminUsers.Commands;
using Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// Both WorkTask FKs onto User are Restrict. A leaver's own tasks go with them;
/// tasks somebody else gave them return to that somebody. Run over SQLite so the
/// constraint is real — the in-memory provider would pass whatever the sweep forgot.
/// </summary>
public class WorkTaskDeleteCleanupTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();
        var collection = new ServiceCollection();
        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        collection.AddIdentityCore<User>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>();
        _services = collection.BuildServiceProvider();
        await Db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();
    private UserManager<User> Users => _services.GetRequiredService<UserManager<User>>();

    private async Task AddUserAsync(string id)
    {
        var created = await Users.CreateAsync(new User { Id = id, UserName = $"{id}@t.local", Email = $"{id}@t.local", DisplayName = id, EmailConfirmed = true });
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
    }

    private static WorkTask NewTask(string createdBy, string assignee, string title) => new()
    {
        Title = title, DepartmentId = 1, CreatedById = createdBy, AssigneeId = assignee,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task Deleting_a_user_drops_their_tasks_and_hands_back_the_ones_they_were_given()
    {
        await AddUserAsync("u-admin");
        await AddUserAsync("u-leaver");
        await AddUserAsync("u-other");
        Db.Departments.Add(new Department { Id = 1, Name = "Sales", Code = "SAL" });
        Db.WorkTasks.AddRange(
            NewTask("u-leaver", "u-other", "created by leaver"),
            NewTask("u-other", "u-leaver", "given to leaver"),
            NewTask("u-leaver", "u-leaver", "leaver's own"));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var result = await new DeleteAdminUser.Handler(Db, Users).Handle(
            new DeleteAdminUser.Command { Id = "u-leaver", RequestingUserId = "u-admin" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var left = await Db.WorkTasks.AsNoTracking().ToListAsync();
        var handedBack = Assert.Single(left);
        Assert.Equal("given to leaver", handedBack.Title);
        Assert.Equal("u-other", handedBack.AssigneeId);
    }
}
