using Application.Core;
using Application.WorkTasks.Commands;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// The System Administrator decides which task fields are asked. A required field is
/// refused blank on create and on edit; a hidden one is not asked, stored as nothing on
/// create, and left as it was on edit — nobody can see it to clear it.
/// </summary>
public class WorkTaskFieldRuleTests
{
    private readonly FakeEmailService _email = new();

    private Task<Result<WorkTaskDto>> Create(AppDbContext db, UpsertWorkTaskRequest task) =>
        new CreateWorkTask.Handler(db, _email, NullLogger<CreateWorkTask.Handler>.Instance)
            .Handle(new CreateWorkTask.Command { CallerUserId = Hr, Task = task }, CancellationToken.None);

    private Task<Result<WorkTaskDto>> Update(AppDbContext db, int id, UpsertWorkTaskRequest task) =>
        new UpdateWorkTask.Handler(db, _email, NullLogger<UpdateWorkTask.Handler>.Instance)
            .Handle(new UpdateWorkTask.Command { Id = id, CallerUserId = Hr, Task = task }, CancellationToken.None);

    private static UpsertWorkTaskRequest Request() => new()
    {
        Title = "Chase notes",
        DepartmentId = Sales,
        ProjectId = SalesProject,
        AssigneeIds = [SalesManager],
        Priority = WorkTaskPriority.High,
        IsBillable = true,
    };

    private static async Task Configure(AppDbContext db, Action<WorkTaskSettings> change)
    {
        var row = await db.WorkTaskSettings.SingleAsync();
        change(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData("Description")]
    [InlineData("DueDate")]
    [InlineData("TargetHours")]
    public async Task A_required_field_is_refused_blank(string field)
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s =>
        {
            if (field == "Description") s.DescriptionRequirement = FieldRequirement.Required;
            if (field == "DueDate") s.DueDateRequirement = FieldRequirement.Required;
            if (field == "TargetHours") s.TargetHoursRequirement = FieldRequirement.Required;
        });

        var result = await Create(db, Request());

        var expected = field switch
        {
            "Description" => WorkTaskFieldRules.DescriptionRequiredMessage,
            "DueDate" => WorkTaskFieldRules.DueDateRequiredMessage,
            _ => WorkTaskFieldRules.TargetHoursRequiredMessage,
        };
        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public async Task Whitespace_is_not_a_description()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.DescriptionRequirement = FieldRequirement.Required);
        var request = Request();
        request.Description = "   ";

        Assert.Equal(WorkTaskFieldRules.DescriptionRequiredMessage, (await Create(db, request)).Error);
    }

    [Fact]
    public async Task An_optional_project_may_be_left_out()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.ProjectRequirement = FieldRequirement.Optional);
        var request = Request();
        request.ProjectId = null;

        var result = await Create(db, request);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.ProjectId);
    }

    [Fact]
    public async Task A_required_project_is_refused_blank()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var request = Request();
        request.ProjectId = null;

        Assert.Equal(WorkTaskFieldRules.ProjectRequiredMessage, (await Create(db, request)).Error);
    }

    [Fact]
    public async Task A_required_billable_is_refused_blank()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var request = Request();
        request.IsBillable = null;

        Assert.Equal(WorkTaskFieldRules.BillableRequiredMessage, (await Create(db, request)).Error);
    }

    [Fact]
    public async Task A_hidden_field_is_stored_as_nothing_on_create()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s =>
        {
            s.DescriptionRequirement = FieldRequirement.Hidden;
            s.DueDateRequirement = FieldRequirement.Hidden;
            s.TargetHoursRequirement = FieldRequirement.Hidden;
            s.BillableRequirement = FieldRequirement.Hidden;
            s.ShowPriority = false;
        });
        var request = Request();
        request.Description = "sneaked in";
        request.DueDate = new DateOnly(2026, 10, 1);
        request.TargetHours = 8;

        var result = await Create(db, request);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await db.WorkTasks.AsNoTracking().SingleAsync();
        Assert.Null(stored.Description);
        Assert.Null(stored.DueDate);
        Assert.Null(stored.TargetHours);
        Assert.Null(stored.IsBillable);
        Assert.Equal(WorkTaskPriority.Normal, stored.Priority);
    }

    [Fact]
    public async Task A_hidden_field_survives_an_edit()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, SalesManager);
        task.Description = "Keep me";
        task.DueDate = new DateOnly(2026, 10, 1);
        task.TargetHours = 16;
        task.IsBillable = true;
        task.Priority = WorkTaskPriority.High;
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Configure(db, s =>
        {
            s.DescriptionRequirement = FieldRequirement.Hidden;
            s.DueDateRequirement = FieldRequirement.Hidden;
            s.TargetHoursRequirement = FieldRequirement.Hidden;
            s.BillableRequirement = FieldRequirement.Hidden;
            s.ShowPriority = false;
        });
        // What the dialog sends with those fields hidden: nothing for them.
        var request = Request();
        request.Title = "Typo fixed";
        request.Description = null;
        request.DueDate = null;
        request.TargetHours = null;
        request.IsBillable = null;
        request.Priority = WorkTaskPriority.Normal;

        var result = await Update(db, task.Id, request);

        Assert.True(result.IsSuccess, result.Error);
        var stored = await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal("Typo fixed", stored.Title);
        Assert.Equal("Keep me", stored.Description);
        Assert.Equal(new DateOnly(2026, 10, 1), stored.DueDate);
        Assert.Equal(16, stored.TargetHours);
        Assert.True(stored.IsBillable);
        Assert.Equal(WorkTaskPriority.High, stored.Priority);
    }

    [Fact]
    public async Task Unhiding_a_field_as_required_holds_old_tasks_to_it()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var task = NewTask(Sales, Hr, SalesManager);
        task.IsBillable = true;
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Configure(db, s => s.DueDateRequirement = FieldRequirement.Required);
        var request = Request();
        request.Title = "Typo fixed";

        var result = await Update(db, task.Id, request);

        Assert.Equal(WorkTaskFieldRules.DueDateRequiredMessage, result.Error);
    }
}
