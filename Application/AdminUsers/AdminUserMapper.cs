using Application.AdminUsers.DTOs;
using Domain;

namespace Application.AdminUsers;

/// <summary>
/// Builds the response body for every admin-user endpoint.
///
/// Shared so that actions already moved to MediatR and actions still handled in
/// the controller return byte-identical bodies — the frontend types both as
/// AdminUser, and a migration that quietly changed a field would surface as a
/// broken admin panel rather than a failing test.
/// </summary>
public static class AdminUserMapper
{
    public static AdminUserDto ToDto(User user, IEnumerable<string> roles) => new()
    {
        Id = user.Id,
        UserName = user.UserName ?? string.Empty,
        Email = user.Email ?? string.Empty,
        DisplayName = user.DisplayName,
        ImageUrl = user.ImageUrl ?? string.Empty,
        PhoneNumber = user.PhoneNumber,
        DateOfBirth = user.DateOfBirth,
        EmailConfirmed = user.EmailConfirmed,
        Roles = roles.OrderBy(r => r).ToList(),
    };
}
