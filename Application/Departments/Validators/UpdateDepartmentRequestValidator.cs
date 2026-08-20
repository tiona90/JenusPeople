using Application.Departments.Commands;
using FluentValidation;

namespace Application.Departments.Validators;

public class UpdateDepartmentRequestValidator : AbstractValidator<UpdateDepartment.Command>
{
    public UpdateDepartmentRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.");

        RuleFor(x => x.Department)
            .NotNull()
            .WithMessage("Department payload is required.")
            .SetValidator(new UpsertDepartmentRequestValidator());
    }
}
