using Application.AdminUsers.Commands;
using Domain;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AdminUsers.Validators;

/// <summary>
/// The checks CreateUser ran inline, in the layer the rest of the app keeps them.
/// Same rules and the same messages, so the admin panel shows what it showed
/// before — only the status code differs, and only where it was wrong: these are
/// 400s, while "email is already registered" is a 409 the handler raises.
/// </summary>
public class CreateAdminUserValidator : AbstractValidator<CreateAdminUser.Command>
{
    public CreateAdminUserValidator(AppDbContext context, RoleManager<Role> roleManager)
    {
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
            RuleFor(x => x.User.JobTitle).MaximumLength(150);

            RuleFor(x => x.User.AnnualLeaveEntitlement)
                .InclusiveBetween(0, 365)
                .When(x => x.User.AnnualLeaveEntitlement.HasValue);

            RuleFor(x => x.User.DepartmentId)
                .MustAsync(async (departmentId, cancellationToken) =>
                    await context.Departments.AnyAsync(d => d.Id == departmentId, cancellationToken))
                .WithMessage("Selected department does not exist.");

            RuleFor(x => x.User.ManagerId)
                .MustAsync(async (managerId, cancellationToken) =>
                    await context.EmployeeProfiles.AnyAsync(ep => ep.Id == managerId, cancellationToken))
                .WithMessage("Manager profile is invalid.")
                .When(x => !string.IsNullOrWhiteSpace(x.User.ManagerId));

            // Exactly one role per user. The DTO carries [MaxLength(1)] for requests
            // bound by MVC; this is what enforces it for the command itself, which
            // is what CreateUser used to call a "backstop".
            RuleFor(x => x.User.Roles)
                .Must(roles => CountDistinct(roles) <= 1)
                .WithMessage("A user can have only one role.");

            RuleFor(x => x.User.Roles)
                .MustAsync(async (roles, cancellationToken) =>
                {
                    var requested = Distinct(roles);
                    if (requested.Count == 0) return true;

                    var known = await roleManager.Roles
                        .Where(role => role.Name != null)
                        .Select(role => role.Name!)
                        .ToListAsync(cancellationToken);

                    var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
                    return requested.All(knownSet.Contains);
                })
                .WithMessage("One or more roles are invalid.");
        });
    }

    private static List<string> Distinct(IEnumerable<string>? roles) =>
        (roles ?? [])
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int CountDistinct(IEnumerable<string>? roles) => Distinct(roles).Count;
}
