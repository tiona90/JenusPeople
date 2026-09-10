using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Children.Support;

/// <summary>
/// Guards the one invariant on <see cref="EmployeeProfile.HasChildren"/>: it cannot
/// say "no children" while children are on the profile. A flag free to contradict
/// the rows beside it is the failure mode CLAUDE.md documents for every duplicated
/// leave figure — so the contradiction is refused rather than quietly resolved in
/// either direction.
/// </summary>
public static class HasChildrenDeclaration
{
    /// <summary>
    /// Applies a requested declaration to the profile, or returns an error message
    /// when it contradicts the children already on it. A <c>null</c> request leaves
    /// the stored answer alone — an older client that does not send the field must
    /// not silently retract a declaration. Both the validation and the assignment
    /// live here, in one place, so the rule cannot drift between what is checked
    /// and what is saved.
    /// </summary>
    public static async Task<string?> ApplyAsync(
        AppDbContext context,
        EmployeeProfile profile,
        bool? requested,
        CancellationToken cancellationToken)
    {
        if (requested is null)
            return null;

        if (requested is false)
        {
            var childCount = await context.Children
                .CountAsync(child => child.EmployeeProfileId == profile.Id, cancellationToken);

            if (childCount > 0)
                return $"Remove the {childCount} child(ren) on your profile before saying you have none.";
        }

        profile.HasChildren = requested.Value;
        return null;
    }
}
