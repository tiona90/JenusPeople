using Application.Core;
using Application.TaskSettings;
using Application.TaskSettings.Commands;
using Application.TaskSettings.Validators;
using Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static WorkTrack.Tests.WorkTasks.WorkTaskWorld;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// The System Administrator's Task Settings: one row, created by the migration with
/// today's behaviour, so nothing changes until somebody edits it.
/// </summary>
public class WorkTaskSettingsTests
{
    [Fact]
    public async Task The_database_starts_with_one_row_of_todays_behaviour()
    {
        await using var db = await TransactionalTestDb.CreateAsync();

        var row = Assert.Single(await db.WorkTaskSettings.AsNoTracking().ToListAsync());

        Assert.Equal(WorkTaskSettings.SingletonId, row.Id);
        Assert.Equal(FieldRequirement.Optional, row.DescriptionRequirement);
        Assert.Equal(FieldRequirement.Optional, row.DueDateRequirement);
        Assert.Equal(FieldRequirement.Optional, row.TargetHoursRequirement);
        Assert.Equal(FieldRequirement.Optional, row.AttachmentsRequirement);
        Assert.Equal(FieldRequirement.Required, row.ProjectRequirement);
        Assert.Equal(FieldRequirement.Required, row.BillableRequirement);
        Assert.True(row.ShowPriority);
        Assert.True(row.RequireCompletionConfirmation);
        Assert.Equal(10, row.MaxAttachmentsPerTask);
        Assert.Equal(10, row.MaxAttachmentSizeMb);
        Assert.True(row.AllowImages && row.AllowPdf && row.AllowWord && row.AllowExcel);
    }

    [Fact]
    public async Task With_no_row_the_loader_reads_the_defaults()
    {
        await using var db = TestDb.Create(); // in-memory: HasData is not applied without EnsureCreated

        var settings = await WorkTaskSettingsStore.LoadAsync(db, CancellationToken.None);

        Assert.Equal(FieldRequirement.Required, settings.ProjectRequirement);
        Assert.True(settings.RequireCompletionConfirmation);
        Assert.Equal(10, settings.MaxAttachmentsPerTask);
    }

    private static Task<Result<WorkTaskSettingsDto>> Save(Persistence.AppDbContext db, WorkTaskSettingsDto dto) =>
        new UpdateWorkTaskSettings.Handler(db).Handle(
            new UpdateWorkTaskSettings.Command { Settings = dto, NowUtc = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc) },
            CancellationToken.None);

    private static WorkTaskSettingsDto Defaults() => WorkTaskSettingsDto.From(new WorkTaskSettings());

    [Fact]
    public async Task Saving_replaces_every_setting()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        var dto = Defaults();
        dto.DueDateRequirement = FieldRequirement.Required;
        dto.BillableRequirement = FieldRequirement.Hidden;
        dto.MaxAttachmentsPerTask = 3;
        dto.AllowExcel = false;

        var result = await Save(db, dto);

        Assert.True(result.IsSuccess, result.Error);
        var row = await db.WorkTaskSettings.AsNoTracking().SingleAsync();
        Assert.Equal(FieldRequirement.Required, row.DueDateRequirement);
        Assert.Equal(FieldRequirement.Hidden, row.BillableRequirement);
        Assert.Equal(3, row.MaxAttachmentsPerTask);
        Assert.False(row.AllowExcel);
    }

    [Theory]
    [InlineData(nameof(WorkTaskSettingsDto.ProjectRequirement), FieldRequirement.Hidden)]
    [InlineData(nameof(WorkTaskSettingsDto.BillableRequirement), FieldRequirement.Optional)]
    public void A_field_refuses_a_value_outside_its_set(string field, FieldRequirement value)
    {
        var dto = Defaults();
        typeof(WorkTaskSettingsDto).GetProperty(field)!.SetValue(dto, value);

        var result = new UpdateWorkTaskSettingsValidator().Validate(new UpdateWorkTaskSettings.Command { Settings = dto });

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(21, 10, true)]
    [InlineData(10, 0, true)]
    [InlineData(10, 11, true)]
    [InlineData(10, 10, false)] // no kind allowed
    public void Limits_are_bounded(int count, int mb, bool kinds)
    {
        var dto = Defaults();
        dto.MaxAttachmentsPerTask = count;
        dto.MaxAttachmentSizeMb = mb;
        if (!kinds) dto.AllowImages = dto.AllowPdf = dto.AllowWord = dto.AllowExcel = false;

        var result = new UpdateWorkTaskSettingsValidator().Validate(new UpdateWorkTaskSettings.Command { Settings = dto });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Switching_confirmation_off_closes_waiting_tasks()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var waiting = NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.AwaitingConfirmation);
        waiting.SentBackReason = "Missing the totals";
        waiting.SentBackAtUtc = new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc);
        var open = NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.InProgress);
        db.WorkTasks.AddRange(waiting, open);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var dto = Defaults();
        dto.RequireCompletionConfirmation = false;

        var result = await Save(db, dto);

        Assert.True(result.IsSuccess, result.Error);
        var closed = await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == waiting.Id);
        Assert.Equal(WorkTaskStatus.Done, closed.Status);
        Assert.Equal(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc), closed.CompletedAtUtc);
        Assert.Null(closed.ConfirmedById);
        Assert.Null(closed.SentBackReason);
        Assert.Null(closed.SentBackAtUtc);
        Assert.Equal(WorkTaskStatus.InProgress, (await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == open.Id)).Status);
    }

    [Fact]
    public async Task Switching_confirmation_on_moves_nothing()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        await SeedAsync(db);
        var row = await db.WorkTaskSettings.SingleAsync();
        row.RequireCompletionConfirmation = false;
        var done = NewTask(Sales, Hr, SalesManager, status: WorkTaskStatus.Done);
        db.WorkTasks.Add(done);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await Save(db, Defaults()); // confirmation back on

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(WorkTaskStatus.Done, (await db.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == done.Id)).Status);
    }
}
