using System.Text.Json;
using Application.Settings.DTOs;

namespace Application.Settings;

// (De)serialization for the AppSettings.RemindersJson column, with a merge step
// that keeps the stored state aligned to the ReminderDefaults catalogue.
public static class ReminderSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Returns the full catalogue with persisted overrides applied. Defaults are
    // used for any reminder absent from (or invalid in) the stored JSON.
    public static List<ReminderSettingDto> FromJson(string? json)
    {
        var result = ReminderDefaults.Create();
        if (string.IsNullOrWhiteSpace(json)) return result;

        List<ReminderSettingDto>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<List<ReminderSettingDto>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return result; // Corrupt JSON → fall back to defaults rather than throw.
        }
        if (stored is null) return result;

        var byId = stored.Where(s => s.Id is not null)
                         .GroupBy(s => s.Id)
                         .ToDictionary(g => g.Key, g => g.Last());

        foreach (var r in result)
        {
            if (!byId.TryGetValue(r.Id, out var s)) continue;
            r.Enabled = s.Enabled;
            r.Time = NormalizeTime(s.Time, r.Time);
            r.Frequency = ReminderDefaults.Frequencies.Contains(s.Frequency) ? s.Frequency : r.Frequency;
        }
        return result;
    }

    // Merges the incoming list onto the defaults (discarding unknown Ids and
    // sanitising values) and serialises the result.
    public static string ToJson(IEnumerable<ReminderSettingDto>? reminders)
    {
        var byId = (reminders ?? Enumerable.Empty<ReminderSettingDto>())
            .Where(r => r.Id is not null && ReminderDefaults.KnownIds.Contains(r.Id))
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.Last());

        var merged = ReminderDefaults.Create();
        foreach (var r in merged)
        {
            if (!byId.TryGetValue(r.Id, out var s)) continue;
            r.Enabled = s.Enabled;
            r.Time = NormalizeTime(s.Time, r.Time);
            r.Frequency = ReminderDefaults.Frequencies.Contains(s.Frequency) ? s.Frequency : r.Frequency;
        }
        return JsonSerializer.Serialize(merged, JsonOptions);
    }

    // Accepts "H:mm"/"HH:mm"; returns canonical "HH:mm" or the fallback.
    private static string NormalizeTime(string? value, string fallback)
    {
        if (TimeOnly.TryParse(value, out var t)) return t.ToString("HH:mm");
        return fallback;
    }
}
