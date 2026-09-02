using Application.Projects.DTOs;
using FluentValidation;

namespace Application.Projects.Validators;

public class UpsertProjectRequestValidator : AbstractValidator<UpsertProjectRequest>
{
    public UpsertProjectRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(150).WithMessage("Project name must not exceed 150 characters.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Project code is required.")
            .MaximumLength(20).WithMessage("Project code must not exceed 20 characters.");

        // A project with no department reaches nobody, so it is refused at the
        // door rather than saved and later reported missing.
        RuleFor(x => x.DepartmentIds)
            .NotEmpty().WithMessage("Select at least one department.");

        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.ColorKey).MaximumLength(8);
        RuleFor(x => x.TargetWeeklyHours).InclusiveBetween(0, 1000);
        RuleFor(x => x.TargetMonthlyHours).InclusiveBetween(0, 5000);
    }
}
