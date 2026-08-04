using System.ComponentModel.DataAnnotations;
using Application.AdminUsers.DTOs;
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A user holds exactly one role. The admin UI enforces this with radio buttons,
/// but the payloads have to reject a multi-role assignment too — otherwise the
/// rule is only skin-deep and a hand-crafted request can still hand someone both
/// Manager and Employee, which makes department scoping ambiguous.
/// </summary>
public class SingleRolePerUserTests
{
    private static IReadOnlyList<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    private static AdminCreateUserDto CreateDto(params string[] roles) => new()
    {
        Email = "newjoiner@example.test",
        DisplayName = "New Joiner",
        DepartmentId = 1,
        Roles = [.. roles],
    };

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.Manager)]
    [InlineData(AppRoles.Employee)]
    public void Creating_a_user_with_one_role_is_valid(string role)
    {
        var results = Validate(CreateDto(role));

        Assert.Empty(results);
    }

    [Fact]
    public void Creating_a_user_with_two_roles_is_rejected()
    {
        var results = Validate(CreateDto(AppRoles.Manager, AppRoles.Employee));

        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(AdminCreateUserDto.Roles))
            && r.ErrorMessage == "A user can have only one role.");
    }

    [Fact]
    public void Creating_a_user_with_no_role_is_rejected()
    {
        var results = Validate(CreateDto());

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AdminCreateUserDto.Roles)));
    }

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.Manager)]
    [InlineData(AppRoles.Employee)]
    public void Reassigning_to_one_role_is_valid(string role)
    {
        var results = Validate(new AdminSetUserRolesDto { Roles = [role] });

        Assert.Empty(results);
    }

    [Fact]
    public void Reassigning_to_two_roles_is_rejected()
    {
        var results = Validate(new AdminSetUserRolesDto { Roles = [AppRoles.Admin, AppRoles.Manager] });

        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(AdminSetUserRolesDto.Roles))
            && r.ErrorMessage == "A user can have only one role.");
    }

    [Fact]
    public void Reassigning_to_no_role_is_rejected()
    {
        var results = Validate(new AdminSetUserRolesDto { Roles = [] });

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AdminSetUserRolesDto.Roles)));
    }

    /// <summary>
    /// All three roles must remain individually assignable — the cap is one role
    /// per user, not a shrunken set of roles.
    /// </summary>
    [Fact]
    public void All_three_roles_stay_assignable()
    {
        string[] roles = [AppRoles.Admin, AppRoles.Manager, AppRoles.Employee];

        Assert.Equal(3, roles.Distinct().Count());
        Assert.All(roles, role => Assert.Empty(Validate(new AdminSetUserRolesDto { Roles = [role] })));
    }
}
