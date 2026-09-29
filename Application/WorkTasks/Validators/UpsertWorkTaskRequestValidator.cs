using Application.WorkTasks.Commands;
using Application.WorkTasks.DTOs;
using Domain;
using FluentValidation;

namespace Application.WorkTasks.Validators;

public class UpsertWorkTaskRequestValidator : AbstractValidator<UpsertWorkTaskRequest>
{
    // Caps a hand-picked list only. An empty list is everyone in the department,
    // expanded by WorkTaskAssigneeRule.ResolveAsync, and is not held to it.
    public const int MaxAssignees = 20;

    public UpsertWorkTaskRequestValidator()
    {
        RuleFor(x => x.Title)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Title is required.")
            .MaximumLength(WorkTask.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(WorkTask.DescriptionMaxLength);
        RuleFor(x => x.DepartmentId).GreaterThan(0).WithMessage("Department is required.");
        // Whether a project is required is a Task Setting (WorkTaskFieldRules); a given one must be real.
        RuleFor(x => x.ProjectId).GreaterThan(0).When(x => x.ProjectId.HasValue).WithMessage("Project is required.");
        RuleFor(x => x.AssigneeIds)
            .NotNull()
            .Must(ids => ids.Count <= MaxAssignees).WithMessage($"A task can have at most {MaxAssignees} assignees.")
            .Must(ids => ids.All(id => !string.IsNullOrWhiteSpace(id))).WithMessage("An assignee is blank.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("The same person is assigned twice.");
        RuleFor(x => x.Priority).IsInEnum();
        // Billable is now a Task Setting as well; see WorkTaskFieldRules.
        RuleFor(x => x.TargetHours).InclusiveBetween(1, WorkTask.MaxTargetHours)
            .When(x => x.TargetHours.HasValue)
            .WithMessage($"Target hours must be between 1 and {WorkTask.MaxTargetHours}.");
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
