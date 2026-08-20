using Application.Departments.Commands;
using FluentValidation;

namespace Application.Departments.Validators;

public class CreateDepartmentRequestValidator : AbstractValidator<CreateDepartment.Command>
{
    public CreateDepartmentRequestValidator()
    {
        RuleFor(x => x.Department)
            .NotNull()
            .WithMessage("Department payload is required.")
            .SetValidator(new UpsertDepartmentRequestValidator());
    }
}
