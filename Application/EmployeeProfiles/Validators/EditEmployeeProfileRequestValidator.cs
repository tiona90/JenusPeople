using Application.EmployeeProfiles.Commands;
using Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.EmployeeProfiles.Validators;

public class EditEmployeeProfileRequestValidator : AbstractValidator<EditEmployeeProfile.Command>
{
    public EditEmployeeProfileRequestValidator(AppDbContext context)
    {
        RuleFor(x => x.EmployeeProfile)
            .NotNull()
            .WithMessage("EmployeeProfile payload is required.");

        When(x => x.EmployeeProfile is not null, () =>
        {
            RuleFor(x => x.EmployeeProfile.Id)
                .NotEmpty()
                .WithMessage("Id is required.")
                .MustAsync(async (id, cancellationToken) =>
                    await context.EmployeeProfiles.AnyAsync(ep => ep.Id == id, cancellationToken))
                .WithMessage("Employee profile does not exist.");

            // Whether a department is required depends on the role of the user whose
            // profile this is, so it takes a lookup rather than a standalone rule.
            // An Admin sees every department and belongs to none; everyone else is
            // placed in one, which is where their manager, their leave routing and
            // their project visibility all come from.
            //
            // This is also the path a role change takes: the edit dialog sets roles
            // first and saves the profile second, so the role read here is already
            // the new one — a promotion to Admin arrives with a null department and
            // a demotion out of it arrives with a real one.
            RuleFor(x => x.EmployeeProfile)
                .CustomAsync(async (request, validationContext, cancellationToken) =>
                {
                    var isAdmin = await context.EmployeeProfiles
                        .AsNoTracking()
                        .AnyAsync(ep =>
                            ep.Id == request.Id
                            && ep.User != null
                            && ep.User.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin),
                            cancellationToken);

                    if (isAdmin)
                    {
                        if (request.DepartmentId is not null)
                        {
                            validationContext.AddFailure(
                                "EmployeeProfile.DepartmentId",
                                "An Admin cannot belong to a department.");
                        }

                        return;
                    }

                    if (request.DepartmentId is not { } departmentId)
                    {
                        validationContext.AddFailure(
                            "EmployeeProfile.DepartmentId",
                            "DepartmentId is required.");
                        return;
                    }

                    var exists = await context.Departments
                        .AnyAsync(d => d.Id == departmentId && d.IsActive, cancellationToken);

                    if (!exists)
                    {
                        validationContext.AddFailure(
                            "EmployeeProfile.DepartmentId",
                            "Department is invalid or inactive.");
                    }
                });

            RuleFor(x => x.EmployeeProfile.ManagerId)
                .MustAsync(async (request, managerId, cancellationToken) =>
                {
                    if (string.IsNullOrWhiteSpace(managerId)) return true;
                    if (managerId == request.EmployeeProfile.Id) return false;

                    return await context.EmployeeProfiles.AnyAsync(ep => ep.Id == managerId, cancellationToken);
                })
                .WithMessage("Manager profile is invalid.");

            RuleFor(x => x.EmployeeProfile.JobTitle)
                .Must(jobTitle => string.IsNullOrEmpty(jobTitle) || !string.IsNullOrWhiteSpace(jobTitle))
                .WithMessage("JobTitle cannot be whitespace only.")
                .MaximumLength(150)
                .WithMessage("JobTitle must not exceed 150 characters.");
        });
    }
}