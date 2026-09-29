using Application.WorkTasks.DTOs;
using Domain;

namespace Application.WorkTasks.Support;

/// <summary>
/// The Task Settings' field rules, applied by CreateWorkTask and UpdateWorkTask.
/// Mirrored by client/src/lib/task-settings.ts — keep the messages identical.
///
/// A hidden field is the one deliberate exception to UpsertWorkTaskRequest being a
/// full replace: on edit the stored value is kept whatever the request carries,
/// because nobody can see the field to clear it, and hiding then un-hiding a field
/// must lose nothing. On create a hidden field is stored as nothing.
/// </summary>
public static class WorkTaskFieldRules
{
    public const string DescriptionRequiredMessage = "Description is required.";
    public const string DueDateRequiredMessage = "Due date is required.";
    public const string TargetHoursRequiredMessage = "Target hours are required.";
    public const string ProjectRequiredMessage = "Project is required.";
    public const string BillableRequiredMessage = "Say whether the task is billable.";
    public const string AttachmentRequiredMessage = "Attach a file before marking this task done.";

    public static string? Check(WorkTaskSettings s, UpsertWorkTaskRequest input)
    {
        if (s.ProjectRequirement == FieldRequirement.Required && input.ProjectId is null) return ProjectRequiredMessage;
        if (s.BillableRequirement == FieldRequirement.Required && input.IsBillable is null) return BillableRequiredMessage;
        if (s.DescriptionRequirement == FieldRequirement.Required && string.IsNullOrWhiteSpace(input.Description)) return DescriptionRequiredMessage;
        if (s.DueDateRequirement == FieldRequirement.Required && input.DueDate is null) return DueDateRequiredMessage;
        if (s.TargetHoursRequirement == FieldRequirement.Required && input.TargetHours is null) return TargetHoursRequiredMessage;
        return null;
    }

    public static void ApplyHiddenOnCreate(WorkTaskSettings s, UpsertWorkTaskRequest input)
    {
        if (s.DescriptionRequirement == FieldRequirement.Hidden) input.Description = null;
        if (s.DueDateRequirement == FieldRequirement.Hidden) input.DueDate = null;
        if (s.TargetHoursRequirement == FieldRequirement.Hidden) input.TargetHours = null;
        if (s.BillableRequirement == FieldRequirement.Hidden) input.IsBillable = null;
        if (!s.ShowPriority) input.Priority = WorkTaskPriority.Normal;
    }

    public static void KeepHiddenOnEdit(WorkTaskSettings s, UpsertWorkTaskRequest input, WorkTask stored)
    {
        if (s.DescriptionRequirement == FieldRequirement.Hidden) input.Description = stored.Description;
        if (s.DueDateRequirement == FieldRequirement.Hidden) input.DueDate = stored.DueDate;
        if (s.TargetHoursRequirement == FieldRequirement.Hidden) input.TargetHours = stored.TargetHours;
        if (s.BillableRequirement == FieldRequirement.Hidden) input.IsBillable = stored.IsBillable;
        if (!s.ShowPriority) input.Priority = stored.Priority;
    }

    /// <summary>A task under a required-attachment rule cannot be closed without a file.</summary>
    public static bool NeedsAttachment(WorkTaskSettings s, int attachmentCount) =>
        s.AttachmentsRequirement == FieldRequirement.Required && attachmentCount == 0;
}
