using System.ComponentModel.DataAnnotations;
using Domain;

namespace Application.AdminUsers.DTOs;

/// <summary>
/// A full replace, not a patch: <c>UpdateAdminUser</c> assigns every field
/// unconditionally, so a null here genuinely clears the stored value. Anything
/// added must follow that — the "null leaves the stored answer alone" rule used
/// by <c>HasChildrenDeclaration</c> would give a field that refuses to be cleared.
/// </summary>
public class AdminUpdateUserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Phone]
    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Null clears it — the admin dialog's "Not specified" option.</summary>
    public Gender? Gender { get; set; }
}