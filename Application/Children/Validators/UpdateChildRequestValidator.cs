using Application.Children.Commands;
using FluentValidation;

namespace Application.Children.Validators;

/// <summary>See <see cref="CreateChildRequestValidator"/> for why this wrapper exists.</summary>
public class UpdateChildRequestValidator : AbstractValidator<UpdateChild.Command>
{
    public UpdateChildRequestValidator()
    {
        RuleFor(x => x.Child)
            .NotNull()
            .WithMessage("Child payload is required.")
            .SetValidator(new UpsertChildRequestValidator());
    }
}
