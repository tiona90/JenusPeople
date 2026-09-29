using Application.TaskSettings.Commands;
using Domain;
using FluentValidation;

namespace Application.TaskSettings.Validators;

public class UpdateWorkTaskSettingsValidator : AbstractValidator<UpdateWorkTaskSettings.Command>
{
    public UpdateWorkTaskSettingsValidator()
    {
        RuleFor(x => x.Settings).NotNull();
        RuleFor(x => x.Settings.DescriptionRequirement).IsInEnum();
        RuleFor(x => x.Settings.DueDateRequirement).IsInEnum();
        RuleFor(x => x.Settings.TargetHoursRequirement).IsInEnum();
        RuleFor(x => x.Settings.AttachmentsRequirement).IsInEnum();
        // The timesheet's Task column sets a row's project from the task.
        RuleFor(x => x.Settings.ProjectRequirement)
            .Must(r => r is FieldRequirement.Required or FieldRequirement.Optional)
            .WithMessage("Project can be required or optional, not hidden.");
        // Billable is a yes/no answer: asked, or not asked.
        RuleFor(x => x.Settings.BillableRequirement)
            .Must(r => r is FieldRequirement.Required or FieldRequirement.Hidden)
            .WithMessage("Billable can be required or hidden, not optional.");
        RuleFor(x => x.Settings.MaxAttachmentsPerTask)
            .InclusiveBetween(1, WorkTaskSettings.MaxAttachmentsCeiling)
            .WithMessage($"A task can be allowed 1 to {WorkTaskSettings.MaxAttachmentsCeiling} attachments.");
        RuleFor(x => x.Settings.MaxAttachmentSizeMb)
            .InclusiveBetween(1, WorkTaskSettings.MaxAttachmentSizeMbCeiling)
            .WithMessage($"The size limit must be 1 to {WorkTaskSettings.MaxAttachmentSizeMbCeiling} MB.");
        RuleFor(x => x.Settings)
            .Must(s => s.AllowImages || s.AllowPdf || s.AllowWord || s.AllowExcel)
            .WithMessage("Allow at least one kind of file.");
    }
}
