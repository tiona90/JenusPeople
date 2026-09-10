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
    /// An error message when the requested declaration contradicts the profile's
    /// children, or <c>null</c> when it may be saved. A <c>null</c> request leaves
    /// the stored answer alone — a client that does not send the field must not
    /// silently retract a declaration.
    /// </summary>
    public static async Task<string?> ValidateAsync(
        AppDbContext context,
        EmployeeProfile profile,
        bool? requested,
        CancellationToken cancellationToken)
    {
        if (requested is not false)
            return null;

        var childCount = await context.Children
            .CountAsync(child => child.EmployeeProfileId == profile.Id, cancellationToken);

        return childCount == 0
            ? null
            : $"Remove the {childCount} child(ren) on your profile before saying you have none.";
    }
}
