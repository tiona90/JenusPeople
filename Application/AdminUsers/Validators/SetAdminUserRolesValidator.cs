using Application.AdminUsers.Commands;
using Domain;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.AdminUsers.Validators;

/// <summary>
/// One real role per user. Role assignment decides department scoping, so this is
/// worth guarding here as well as through the DTO annotations that MVC enforces —
/// the controller called its own copy a backstop for the same reason.
/// </summary>
public class SetAdminUserRolesValidator : AbstractValidator<SetAdminUserRoles.Command>
{
    public SetAdminUserRolesValidator(RoleManager<Role> roleManager)
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("User id is required.");

        RuleFor(x => x.Roles)
            .NotNull()
            .WithMessage("Roles payload is required.");

        When(x => x.Roles is not null, () =>
        {
            RuleFor(x => x.Roles.Roles)
                .Must(roles => Distinct(roles).Count > 0)
                .WithMessage("A role is required.");

            RuleFor(x => x.Roles.Roles)
                .Must(roles => Distinct(roles).Count <= 1)
                .WithMessage("A user can have only one role.");

            RuleFor(x => x.Roles.Roles)
                .MustAsync(async (roles, cancellationToken) =>
                {
                    var requested = Distinct(roles);
                    if (requested.Count == 0) return true;

                    var known = await roleManager.Roles
                        .Where(role => role.Name != null)
                        .Select(role => role.Name!)
                        .ToListAsync(cancellationToken);

                    return requested.All(new HashSet<string>(known, StringComparer.OrdinalIgnoreCase).Contains);
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
}
