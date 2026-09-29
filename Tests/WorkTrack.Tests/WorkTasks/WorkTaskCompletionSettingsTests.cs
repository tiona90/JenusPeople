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
/// The Task Settings' completion rules: confirmation can be switched off, a required
/// attachment gates Done rather than creation, and the attachment limits are the
/// System Administrator's — narrowing StoreFile's policy, never widening it.
/// </summary>
public class WorkTaskCompletionSettingsTests
{
    private readonly FakeEmailService _email = new();

    private Task<Result<WorkTaskDto>> SetStatus(AppDbContext db, int id, string caller, WorkTaskStatus status) =>
        new UpdateWorkTaskStatus.Handler(db, _email, NullLogger<UpdateWorkTaskStatus.Handler>.Instance)
            .Handle(new UpdateWorkTaskStatus.Command { Id = id, CallerUserId = caller, Status = status }, CancellationToken.None);

    private static Task<Result<WorkTaskDto>> Attach(AppDbContext db, int id, byte[] content, string name) =>
        new AddWorkTaskAttachment.Handler(db).Handle(new AddWorkTaskAttachment.Command
        {
            Id = id, CallerUserId = Hr, Content = content, FileName = name,
        }, CancellationToken.None);

    private static async Task Configure(AppDbContext db, Action<WorkTaskSettings> change)
    {
        var row = await db.WorkTaskSettings.SingleAsync();
        change(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<int> Seeded(AppDbContext db, WorkTask task)
    {
        db.WorkTasks.Add(task);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return task.Id;
    }

    private static async Task<WorkTask> Reload(AppDbContext db, int id) =>
        await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == id);

    // A real PNG signature padded past StoreFile's 100-byte floor.
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[200]];

    // A real PDF signature, padded after the header up to the requested size.
    private static byte[] Pdf(int size)
    {
        var signature = "%PDF-"u8.ToArray();
        var buffer = new byte[Math.Max(size, signature.Length)];
        signature.CopyTo(buffer, 0);
        return buffer;
    }

    [Fact]
    public async Task With_confirmation_off_an_assignees_done_closes_the_task()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.RequireCompletionConfirmation = false);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.Done, result.Value!.Status);
        Assert.NotNull(result.Value.CompletedAtUtc);
        Assert.Empty(_email.Sent);
        // Nobody confirmed it — confirmation is off and the closer is a plain assignee.
        Assert.Null((await Reload(db, id)).ConfirmedById);
    }

    [Fact]
    public async Task A_required_attachment_refuses_done_without_a_file()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.AttachmentsRequirement = FieldRequirement.Required);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkTaskFieldRules.AttachmentRequiredMessage, result.Error);
    }

    [Fact]
    public async Task A_required_attachment_refuses_confirm_without_a_file()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation));
        await Configure(db, s => s.AttachmentsRequirement = FieldRequirement.Required);

        var result = await SetStatus(db, id, Hr, WorkTaskStatus.Done);

        Assert.Equal(WorkTaskFieldRules.AttachmentRequiredMessage, result.Error);
    }

    [Fact]
    public async Task A_required_attachment_does_not_stop_cancelling()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.AttachmentsRequirement = FieldRequirement.Required);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));

        var result = await SetStatus(db, id, Hr, WorkTaskStatus.Cancelled);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task With_a_file_a_required_attachment_lets_done_through()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.AttachmentsRequirement = FieldRequirement.Required);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress));
        Assert.True((await Attach(db, id, Pdf(2_000), "brief.pdf")).IsSuccess);

        var result = await SetStatus(db, id, SalesManager, WorkTaskStatus.Done);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.AwaitingConfirmation, result.Value!.Status);
    }

    [Fact]
    public async Task The_configured_count_limits_uploads()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.MaxAttachmentsPerTask = 2);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));
        await Attach(db, id, Pdf(2_000), "a.pdf");
        await Attach(db, id, Pdf(2_000), "b.pdf");

        var third = await Attach(db, id, Pdf(2_000), "c.pdf");

        Assert.Equal(AddWorkTaskAttachment.TooManyMessageFor(2), third.Error);
    }

    [Fact]
    public async Task A_task_over_a_lowered_count_keeps_its_files_but_takes_no_more()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));
        for (var i = 0; i < 3; i++) await Attach(db, id, Pdf(2_000), $"{i}.pdf");
        await Configure(db, s => s.MaxAttachmentsPerTask = 1);

        var more = await Attach(db, id, Pdf(2_000), "late.pdf");

        Assert.False(more.IsSuccess);
        Assert.Equal(3, await db.WorkTaskAttachments.CountAsync(a => a.WorkTaskId == id));
    }

    [Fact]
    public async Task The_configured_size_limits_uploads()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.MaxAttachmentSizeMb = 2);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        var result = await Attach(db, id, Pdf(3 * 1024 * 1024), "big.pdf");

        Assert.Equal("That file is larger than the 2MB limit.", result.Error);
    }

    [Fact]
    public async Task A_kind_switched_off_is_refused()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        await Configure(db, s => s.AllowImages = false);
        var id = await Seeded(db, NewTask(Sales, Hr, SalesManager));

        var result = await Attach(db, id, Png(), "shot.png");

        Assert.False(result.IsSuccess);
        Assert.StartsWith("Only ", result.Error);
    }
}
