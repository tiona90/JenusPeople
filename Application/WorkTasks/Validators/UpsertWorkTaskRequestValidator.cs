using Application.WorkTasks.Commands;
using Application.WorkTasks.DTOs;
using Domain;
using FluentValidation;

namespace Application.WorkTasks.Validators;

public class UpsertWorkTaskRequestValidator : AbstractValidator<UpsertWorkTaskRequest>
{
    public const int MaxAssignees = 20;

    public UpsertWorkTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Title is required.")
            .MaximumLength(WorkTask.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(WorkTask.DescriptionMaxLength);
        RuleFor(x => x.DepartmentId).GreaterThan(0).WithMessage("Department is required.");
        RuleFor(x => x.ProjectId).NotNull().GreaterThan(0).WithMessage("Project is required.");
        RuleFor(x => x.AssigneeIds)
            .NotNull()
            .Must(ids => ids.Count > 0).WithMessage("At least one assignee is required.")
            .Must(ids => ids.Count <= MaxAssignees).WithMessage($"A task can have at most {MaxAssignees} assignees.")
            .Must(ids => ids.All(id => !string.IsNullOrWhiteSpace(id))).WithMessage("An assignee is blank.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("The same person is assigned twice.");
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.IsBillable).NotNull().WithMessage("Say whether the task is billable.");
        RuleFor(x => x.TargetHours).InclusiveBetween(1, WorkTask.MaxTargetHours)
            .When(x => x.TargetHours.HasValue)
            .WithMessage($"Target hours must be between 1 and {WorkTask.MaxTargetHours}.");
        RuleFor(x => x.TargetWeeks).InclusiveBetween(1, WorkTask.MaxTargetWeeks)
            .When(x => x.TargetWeeks.HasValue)
            .WithMessage($"Target weeks must be between 1 and {WorkTask.MaxTargetWeeks}.");
    }
}

// ValidationBehavior resolves IValidator<TCommand>, so the request validator only
// runs through these wrappers — see CreateChildRequestValidator for the same shape.
public class CreateWorkTaskValidator : AbstractValidator<CreateWorkTask.Command>
{
    public CreateWorkTaskValidator() =>
        RuleFor(x => x.Task).NotNull().SetValidator(new UpsertWorkTaskRequestValidator());
}

public class UpdateWorkTaskValidator : AbstractValidator<UpdateWorkTask.Command>
{
    public UpdateWorkTaskValidator() =>
        RuleFor(x => x.Task).NotNull().SetValidator(new UpsertWorkTaskRequestValidator());
}

public class UpdateWorkTaskStatusValidator : AbstractValidator<UpdateWorkTaskStatus.Command>
{
    public UpdateWorkTaskStatusValidator() => RuleFor(x => x.Status).IsInEnum();
}
