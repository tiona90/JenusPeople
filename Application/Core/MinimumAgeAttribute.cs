using System.ComponentModel.DataAnnotations;

namespace Application.Core;

/// <summary>
/// The DataAnnotations face of <see cref="PersonFieldRules.ValidDateOfBirth"/>,
/// for the DTOs bound by MVC rather than dispatched as a MediatR command — the
/// employee's own profile update, whose other rules are already attributes
/// (<c>[Phone]</c>, <c>[EmailAddress]</c>).
///
/// Both faces defer to <see cref="PersonFieldRules"/>, so there is one minimum
/// age and one pair of messages however the request arrives. Without this an
/// employee could set a date of birth through their own profile that an admin
/// cannot set for them.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MinimumAgeAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Absent is a refusal, not a pass: the date of birth is required. This
        // reports it rather than leaning on a separate [Required], so all three
        // outcomes carry the wording from PersonFieldRules.
        if (value is not DateOnly dateOfBirth)
        {
            return new ValidationResult(
                PersonFieldRules.DateOfBirthRequiredMessage,
                [validationContext.MemberName ?? string.Empty]);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (dateOfBirth <= PersonFieldRules.LatestAllowedDateOfBirth(today)) return ValidationResult.Success;

        var message = dateOfBirth > today
            ? PersonFieldRules.DateOfBirthFutureMessage
            : PersonFieldRules.DateOfBirthTooYoungMessage;

        return new ValidationResult(message, [validationContext.MemberName ?? string.Empty]);
    }
}
