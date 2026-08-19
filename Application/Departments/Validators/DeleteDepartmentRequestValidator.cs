using Application.Departments.Commands;
using FluentValidation;

namespace Application.Departments.Validators;

public class DeleteDepartmentRequestValidator : AbstractValidator<DeleteDepartment.Command>
{
    public DeleteDepartmentRequestValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0)
            .WithMessage("Id must be greater than 0.");
    }
}
