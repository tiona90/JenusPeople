using System.ComponentModel.DataAnnotations;

namespace Application.AdminUsers.DTOs;

/// <summary>
/// A user holds exactly one role (Admin, Manager or Employee), so this is a
/// single-element list rather than a set. It stays a list because that is the
/// shape Identity's role APIs take, and because loosening it later is easier
/// than tightening it.
/// </summary>
public class AdminSetUserRolesDto
{
    [MinLength(1, ErrorMessage = "A role is required.")]
    [MaxLength(1, ErrorMessage = "A user can have only one role.")]
    public List<string> Roles { get; set; } = new();
}