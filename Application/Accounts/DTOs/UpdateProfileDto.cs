using System.ComponentModel.DataAnnotations;
using Application.Core;

namespace Application.Accounts.DTOs;

public class UpdateProfileDto
{
    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    // Date only (no time). Null clears it. The age rule is the same one the admin
    // dialogs enforce — see Application/Core/PersonFieldRules.cs.
    [MinimumAge]
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>
    /// Whether this employee has children — requirement 1 of the per-child leave
    /// entitlement. Null leaves the stored answer alone (an older client that does
    /// not send the field must not silently retract a declaration).
    ///
    /// Refused as false while children are on the profile: the flag and the list
    /// cannot be allowed to disagree.
    /// </summary>
    public bool? HasChildren { get; set; }

    /// <summary>
    /// The caller's current password — required when <see cref="Email"/> differs
    /// from the stored address, and ignored otherwise. A changed email is a takeover
    /// step (whoever holds the address can reset the password), so a session alone
    /// is not enough to make it.
    /// </summary>
    public string? CurrentPassword { get; set; }
}
