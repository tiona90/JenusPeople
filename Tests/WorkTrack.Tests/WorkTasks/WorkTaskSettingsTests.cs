using Application.TaskSettings;
using Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

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
}
