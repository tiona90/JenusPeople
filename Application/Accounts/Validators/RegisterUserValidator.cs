using Application.Accounts.Commands;
using Domain;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Accounts.Validators;

public class RegisterUserValidator : AbstractValidator<RegisterUser.Command>
{
    public RegisterUserValidator(UserManager<User> userManager, AppDbContext context)
    {
        RuleFor(x => x.Request)
            .NotNull()
            .WithMessage("Registration payload is required.");

        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.DisplayName)
                .NotEmpty().WithMessage("DisplayName is required.")
                .MaximumLength(100).WithMessage("DisplayName must not exceed 100 characters.");

            RuleFor(x => x.Request.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Email is not a valid address.")
                .MustAsync(async (email, cancellationToken) =>
                    await userManager.FindByEmailAsync(email) is null)
                .WithMessage("Email is already registered.");

            RuleFor(x => x.Request.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(6).WithMessage("Password must be at least 6 characters long.");

            RuleFor(x => x.Request.DepartmentId)
                .GreaterThan(0).WithMessage("DepartmentId is required.")
                .MustAsync(async (id, cancellationToken) =>
                    await context.Departments.AnyAsync(d => d.Id == id && d.IsActive, cancellationToken))
                .WithMessage("Registration failed because the selected department is invalid or inactive.");
        });
    }
}
