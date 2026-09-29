using Application.Core;
using Application.Files.Queries;
using Application.WorkTasks.Commands;
using Application.WorkTasks.DTOs;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// Files attached to a task to explain the work. Only whoever may edit the task —
/// its creator, or anyone in scope once the creator is deactivated — attaches or
/// removes one; whoever can see the task may open one. Deleting the task takes its files. SQLite, so the
/// cascades are real.
/// </summary>
public class WorkTaskAttachmentTests
{
    // A real PNG signature padded past StoreFile's 100-byte floor.
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[200]];

    private static async Task<int> Seeded(AppDbContext db, WorkTask task)
    {
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task.Id;
    }

    private static Task<Result<WorkTaskDto>> Attach(AppDbContext db, int taskId, string caller, bool assignedOnly = false,
        string fileName = "brief.png", byte[]? content = null) =>
        new AddWorkTaskAttachment.Handler(db).Handle(new AddWorkTaskAttachment.Command
        {
            Id = taskId, CallerUserId = caller, AssignedOnly = assignedOnly, FileName = fileName, Content = content ?? Png(),
        }, CancellationToken.None);

    private static Task<Result<WorkTaskDto>> Remove(AppDbContext db, int taskId, int attachmentId, string caller, bool assignedOnly = false) =>
        new RemoveWorkTaskAttachment.Handler(db).Handle(new RemoveWorkTaskAttachment.Command
        {
            Id = taskId, AttachmentId = attachmentId, CallerUserId = caller, AssignedOnly = assignedOnly,
        }, CancellationToken.None);

    private static Task<Result<Application.Files.StoredFileDto>> Open(AppDbContext db, string fileId, string caller, bool isManager) =>
        new GetStoredFile.Handler(db).Handle(new GetStoredFile.Query
        {
            Id = fileId, RequestingUserId = caller, IsManager = isManager,
        }, CancellationToken.None);

    private static string FileIdOf(WorkTaskAttachmentDto attachment) => attachment.Url["/api/files/".Length..];

    [Fact]
    public async Task Only_the_creator_attaches()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));

        var byCreator = await Attach(db, id, SalesManager);
        Assert.True(byCreator.IsSuccess, byCreator.Error);
        Assert.Equal("Sam Sales", Assert.Single(byCreator.Value!.Attachments).UploadedByName);
        Assert.Equal("image/png", byCreator.Value.Attachments[0].ContentType);

        // HR in scope and the assignee both see the task, and neither may attach.
        Assert.Equal(ResultErrorKind.Forbidden, (await Attach(db, id, Hr)).ErrorKind);
        Assert.Equal(ResultErrorKind.Forbidden, (await Attach(db, id, Employee, assignedOnly: true)).ErrorKind);
        Assert.Equal(1, await db.StoredFiles.CountAsync());
    }

    [Fact]
    public async Task An_employee_attaches_to_a_task_they_created()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Employee, Employee));

        var result = await Attach(db, id, Employee, assignedOnly: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(Assert.Single(result.Value!.Attachments).CanRemove);
    }

    [Fact]
    public async Task A_deactivated_creators_task_takes_files_from_a_manager_in_scope_but_not_an_employee()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));
        (await db.Users.SingleAsync(u => u.Id == SalesManager)).IsActive = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.True((await Attach(db, id, Hr)).IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, (await Attach(db, id, Employee, assignedOnly: true)).ErrorKind);
    }

    [Fact]
    public async Task An_employee_cannot_attach_to_a_task_they_are_not_on()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, SalesManager));

        var result = await Attach(db, id, Employee, assignedOnly: true);

        Assert.Equal(ResultErrorKind.NotFound, result.ErrorKind);
        Assert.False(await db.StoredFiles.AnyAsync());
    }

    [Fact]
    public async Task A_file_the_upload_rules_refuse_is_not_attached()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));

        var result = await Attach(db, id, SalesManager, fileName: "brief.exe");

        Assert.False(result.IsSuccess);
        Assert.False(await db.WorkTaskAttachments.AnyAsync());
    }

    [Fact]
    public async Task A_task_carries_at_most_ten_files()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));
        for (var i = 0; i < WorkTaskAttachment.MaxPerTask; i++)
            Assert.True((await Attach(db, id, SalesManager)).IsSuccess);

        var result = await Attach(db, id, SalesManager);

        Assert.Equal(AddWorkTaskAttachment.TooManyMessage, result.Error);
        Assert.Equal(WorkTaskAttachment.MaxPerTask, await db.StoredFiles.CountAsync());
    }

    [Fact]
    public async Task The_creator_removes_a_file_and_an_assignee_cannot()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));
        var file = (await Attach(db, id, SalesManager)).Value!.Attachments.Single();

        Assert.Equal(ResultErrorKind.Forbidden, (await Remove(db, id, file.Id, Employee, assignedOnly: true)).ErrorKind);
        Assert.Equal(ResultErrorKind.Forbidden, (await Remove(db, id, file.Id, Hr)).ErrorKind);

        var removed = await Remove(db, id, file.Id, SalesManager);
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.Empty(removed.Value!.Attachments);
        Assert.False(await db.StoredFiles.AnyAsync());
    }

    [Fact]
    public async Task Can_remove_reads_true_for_the_creator_only()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));
        await Attach(db, id, SalesManager);

        var asEmployee = await new Application.WorkTasks.Queries.GetWorkTaskList.Handler(db).Handle(
            new Application.WorkTasks.Queries.GetWorkTaskList.Query { CallerUserId = Employee, AssignedOnly = true }, CancellationToken.None);

        Assert.False(Assert.Single(Assert.Single(asEmployee.Value!).Attachments).CanRemove);
    }

    [Fact]
    public async Task Whoever_can_see_the_task_can_open_its_files()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));
        var fileId = FileIdOf((await Attach(db, id, SalesManager)).Value!.Attachments.Single());

        Assert.True((await Open(db, fileId, Hr, isManager: true)).IsSuccess);
        Assert.True((await Open(db, fileId, Employee, isManager: false)).IsSuccess);
        // Outside the department, the file reads as missing.
        Assert.False((await Open(db, fileId, OpsManager, isManager: true)).IsSuccess);
    }

    [Fact]
    public async Task An_employee_not_on_the_task_cannot_open_its_files()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, SalesManager));
        var fileId = FileIdOf((await Attach(db, id, SalesManager)).Value!.Attachments.Single());

        Assert.False((await Open(db, fileId, Employee, isManager: false)).IsSuccess);
    }

    [Fact]
    public async Task Deleting_the_task_deletes_its_files()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, SalesManager, Employee));
        await Attach(db, id, SalesManager);
        await Attach(db, id, SalesManager);
        db.ChangeTracker.Clear();

        var result = await new DeleteWorkTask.Handler(db).Handle(
            new DeleteWorkTask.Command { Id = id, CallerUserId = SalesManager }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await db.StoredFiles.AnyAsync());
        Assert.False(await db.WorkTaskAttachments.AnyAsync());
    }
}
