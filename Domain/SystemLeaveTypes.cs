namespace Domain;

/// <summary>
/// The leave types the application seeds and depends on by name. They may be
/// reconfigured freely — allowance, notice, attachment policy and the rest are all
/// ordinary settings — but they cannot be renamed or deleted.
///
/// Keyed by name rather than by a stored flag on the row. That is only sound
/// because the name is fixed: <c>UpdateLeaveType</c> refuses to rename one of
/// these, and both create and update already enforce case-insensitive name
/// uniqueness, so exactly one row can ever answer to each of these names. A stored
/// flag would have had to be backfilled by name anyway, and could then drift from
/// the name actually displayed.
///
/// Renaming is refused because other code finds these by name — the client's leave
/// allowance helpers and the Maternity/Paternity eligibility copy among them — and
/// a rename would break that silently rather than loudly. Deleting is refused
/// because seeding does not restore a type an admin removed, so the application
/// would come up missing a leave type nothing offers to recreate.
/// </summary>
public static class SystemLeaveTypes
{
    public const string AnnualLeave = "Annual Leave";
    public const string MaternityLeave = "Maternity Leave";
    public const string PaternityLeave = "Paternity Leave";

    public static readonly string[] All = [AnnualLeave, MaternityLeave, PaternityLeave];

    /// <summary>
    /// Whether <paramref name="name"/> names a protected type. Trimmed and
    /// case-insensitive, matching how the name uniqueness rules compare, so
    /// " annual leave " cannot slip past as a different type.
    /// </summary>
    public static bool IsSystem(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && All.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
}
