namespace Application.Settings.Support;

/// <summary>
/// Parsing and normalising for the working-time settings fields.
///
/// Extracted from UpdateAppSettings so its validator and its handler decide what
/// "mon,tue" or "9:00" mean the same way. They ran the same checks from two copies
/// before, which is the kind of pair that drifts apart one edit at a time.
/// </summary>
internal static class WorkingTimeFormat
{
    /// <summary>Canonical week order for the custom working-days CSV.</summary>
    public static readonly string[] DayOrder = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    public static bool IsKnownDay(string? value) =>
        DayOrder.Contains(value?.Trim().ToLowerInvariant());

    /// <summary>
    /// Accepts "H:mm"/"HH:mm"; emits canonical "HH:mm". Returns false and a
    /// "00:00" placeholder when the input is not a time at all, so callers have to
    /// decide what to do rather than silently storing midnight.
    /// </summary>
    public static bool TryNormalizeTime(string? value, out string normalized)
    {
        if (TimeOnly.TryParse(value, out var parsed))
        {
            normalized = parsed.ToString("HH:mm");
            return true;
        }

        normalized = "00:00";
        return false;
    }

    /// <summary>
    /// Keeps only recognised day tokens, de-duplicated and in week order. An empty
    /// result means nothing in the input was a day name.
    /// </summary>
    public static string NormalizeWorkingDaysCustom(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var set = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant())
            .Where(DayOrder.Contains)
            .ToHashSet();

        return string.Join(",", DayOrder.Where(set.Contains));
    }
}
