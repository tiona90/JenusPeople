using Application.AdminUsers.Commands;
using FluentValidation;

namespace Application.AdminUsers.Validators;

/// <summary>
/// Shape rules only, mirroring AdminUpdateUserDto. Whether the address is taken
/// is the handler's call, since that is a conflict rather than bad input.
/// </summary>
public class UpdateAdminUserValidator : AbstractValidator<UpdateAdminUser.Command>
{
    public UpdateAdminUserValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("User id is required.");

        RuleFor(x => x.User)
            .NotNull()
            .WithMessage("User payload is required.");

        When(x => x.User is not null, () =>
        {
            RuleFor(x => x.User.Email)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .WithMessage("Email is required.")
                .Must(email => !string.IsNullOrWhiteSpace(email))
                .WithMessage("Email is required.")
                .EmailAddress()
                .WithMessage("Email must be a valid email address.");

            RuleFor(x => x.User.DisplayName)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .WithMessage("Display name is required.")
                .Must(name => !string.IsNullOrWhiteSpace(name))
                .WithMessage("Display name is required.")
                .MaximumLength(100)
                .WithMessage("Display name must not exceed 100 characters.");

            RuleFor(x => x.User.PhoneNumber).MaximumLength(30);
        });
    }
}
