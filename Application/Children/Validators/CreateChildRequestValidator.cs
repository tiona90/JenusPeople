using Application.Children.Commands;
using FluentValidation;

namespace Application.Children.Validators;

/// <summary>
/// <see cref="UpsertChildRequestValidator"/> validates <c>UpsertChildRequest</c>,
/// but MediatR's <see cref="Application.Core.ValidationBehavior{TRequest,TResponse}"/>
/// resolves <c>IValidator&lt;TRequest&gt;</c> for the *command* type -- so without this
/// wrapper the inner validator is never resolved and never runs, and a future date
/// of birth reaches the database uncaught.
/// </summary>
public class CreateChildRequestValidator : AbstractValidator<CreateChild.Command>
{
    public CreateChildRequestValidator()
    {
        RuleFor(x => x.Child)
            .NotNull()
            .WithMessage("Child payload is required.")
            .SetValidator(new UpsertChildRequestValidator());
    }
}
