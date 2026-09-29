namespace Domain;

/// <summary>
/// A file attached to a <see cref="WorkTask"/> to explain the work — a brief, a
/// screenshot, a spreadsheet. The bytes live in the <see cref="StoredFile"/>
/// (purpose <see cref="StoredFilePurpose.TaskAttachment"/>), whose uploader is who
/// attached it. Both foreign keys cascade: deleting the task or the file takes
/// this row with it. Deleting the task does not delete the file on its own, so
/// DeleteWorkTask and the user-delete sweeps remove the files explicitly.
/// </summary>
public class WorkTaskAttachment
{
    /// <summary>How many files one task may carry.</summary>
    public const int MaxPerTask = 10;

    public int Id { get; set; }

    public int WorkTaskId { get; set; }
    public WorkTask? WorkTask { get; set; }

    public string StoredFileId { get; set; } = string.Empty;
    public StoredFile? StoredFile { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
