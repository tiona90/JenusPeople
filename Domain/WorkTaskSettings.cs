namespace Domain;

/// <summary>How a configurable task field is treated on the task dialog.</summary>
public enum FieldRequirement
{
    Optional = 0,
    Required = 1,
    Hidden = 2,
}

/// <summary>
/// The System Administrator's organisation-wide rules for tasks: which fields are
/// asked, whether an assignee's Done waits for confirmation, and what files a task may
/// carry. One row (<see cref="SingletonId"/>), created by migration AddWorkTaskSettings
/// with today's behaviour — the seeder does not run on the deployed host. Title and
/// Department are always required and are not here.
/// </summary>
public class WorkTaskSettings
{
    public const int SingletonId = 1;
    public const int MaxAttachmentsCeiling = 20;
    /// <summary>StoreFile's ceiling for a task attachment; the setting may only narrow it.</summary>
    public const int MaxAttachmentSizeMbCeiling = 10;

    public int Id { get; set; } = SingletonId;

    public FieldRequirement DescriptionRequirement { get; set; } = FieldRequirement.Optional;
    public FieldRequirement DueDateRequirement { get; set; } = FieldRequirement.Optional;
    public FieldRequirement TargetHoursRequirement { get; set; } = FieldRequirement.Optional;
    /// <summary>Required gates completion, not creation: the create dialog uploads after the task exists.</summary>
    public FieldRequirement AttachmentsRequirement { get; set; } = FieldRequirement.Optional;
    /// <summary>Required or Optional only: the timesheet's Task column sets a row's project from the task.</summary>
    public FieldRequirement ProjectRequirement { get; set; } = FieldRequirement.Required;
    /// <summary>Required or Hidden only: a yes/no answer is asked or not asked.</summary>
    public FieldRequirement BillableRequirement { get; set; } = FieldRequirement.Required;
    /// <summary>Priority always carries a value; this only decides whether it is shown.</summary>
    public bool ShowPriority { get; set; } = true;

    public bool RequireCompletionConfirmation { get; set; } = true;

    public int MaxAttachmentsPerTask { get; set; } = 10;
    public int MaxAttachmentSizeMb { get; set; } = MaxAttachmentSizeMbCeiling;
    public bool AllowImages { get; set; } = true;
    public bool AllowPdf { get; set; } = true;
    public bool AllowWord { get; set; } = true;
    public bool AllowExcel { get; set; } = true;
}
