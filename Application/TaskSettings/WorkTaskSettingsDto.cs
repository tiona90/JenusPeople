using Domain;

namespace Application.TaskSettings;

/// <summary>The Task Settings as read and as saved — a full replace, like every other settings save.</summary>
public class WorkTaskSettingsDto
{
    public FieldRequirement DescriptionRequirement { get; set; }
    public FieldRequirement DueDateRequirement { get; set; }
    public FieldRequirement TargetHoursRequirement { get; set; }
    public FieldRequirement AttachmentsRequirement { get; set; }
    public FieldRequirement ProjectRequirement { get; set; }
    public FieldRequirement BillableRequirement { get; set; }
    public bool ShowPriority { get; set; }
    public bool RequireCompletionConfirmation { get; set; }
    public int MaxAttachmentsPerTask { get; set; }
    public int MaxAttachmentSizeMb { get; set; }
    public bool AllowImages { get; set; }
    public bool AllowPdf { get; set; }
    public bool AllowWord { get; set; }
    public bool AllowExcel { get; set; }

    public static WorkTaskSettingsDto From(WorkTaskSettings s) => new()
    {
        DescriptionRequirement = s.DescriptionRequirement,
        DueDateRequirement = s.DueDateRequirement,
        TargetHoursRequirement = s.TargetHoursRequirement,
        AttachmentsRequirement = s.AttachmentsRequirement,
        ProjectRequirement = s.ProjectRequirement,
        BillableRequirement = s.BillableRequirement,
        ShowPriority = s.ShowPriority,
        RequireCompletionConfirmation = s.RequireCompletionConfirmation,
        MaxAttachmentsPerTask = s.MaxAttachmentsPerTask,
        MaxAttachmentSizeMb = s.MaxAttachmentSizeMb,
        AllowImages = s.AllowImages,
        AllowPdf = s.AllowPdf,
        AllowWord = s.AllowWord,
        AllowExcel = s.AllowExcel,
    };

    public void ApplyTo(WorkTaskSettings s)
    {
        s.DescriptionRequirement = DescriptionRequirement;
        s.DueDateRequirement = DueDateRequirement;
        s.TargetHoursRequirement = TargetHoursRequirement;
        s.AttachmentsRequirement = AttachmentsRequirement;
        s.ProjectRequirement = ProjectRequirement;
        s.BillableRequirement = BillableRequirement;
        s.ShowPriority = ShowPriority;
        s.RequireCompletionConfirmation = RequireCompletionConfirmation;
        s.MaxAttachmentsPerTask = MaxAttachmentsPerTask;
        s.MaxAttachmentSizeMb = MaxAttachmentSizeMb;
        s.AllowImages = AllowImages;
        s.AllowPdf = AllowPdf;
        s.AllowWord = AllowWord;
        s.AllowExcel = AllowExcel;
    }
}
