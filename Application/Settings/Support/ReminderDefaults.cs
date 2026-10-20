using Application.Settings.DTOs;

namespace Application.Settings.Support;

// Canonical reminder catalogue. The Ids here are the contract shared with the
// client (client-side display metadata is keyed by the same Ids). Stored
// reminder state is always merged onto this list so that:
//   • unknown/stale Ids in the DB are discarded, and
//   • newly-added reminder types appear automatically with their defaults.
public static class ReminderDefaults
{
    public static readonly string[] Frequencies = { "daily", "weekly" };

    public static List<ReminderSettingDto> Create() => new()
    {
        new() { Id = "pending-approvals",  Enabled = true,  Time = "09:00", Frequency = "daily"  },
        new() { Id = "late-submissions",   Enabled = true,  Time = "16:00", Frequency = "weekly" },
        // Replaced "team-alerts", which was catalogued and switched on but had no
        // dispatcher behind it, so it sent nothing. Stored state under the old id
        // is dropped by the merge below and this one appears with its defaults.
        new() { Id = "daily-attendance-report", Enabled = true, Time = "08:00", Frequency = "daily" },
        new() { Id = "low-balance",        Enabled = false, Time = "09:00", Frequency = "weekly" },
        new() { Id = "department-digest",  Enabled = true,  Time = "10:00", Frequency = "weekly" },
        new() { Id = "birthday-reminder",  Enabled = false, Time = "08:30", Frequency = "daily"  },
        new() { Id = "check-in",           Enabled = true,  Time = "09:00", Frequency = "daily"  },
        new() { Id = "check-out",          Enabled = true,  Time = "18:00", Frequency = "daily"  },
        // Added after SeedReminderRunsOnUpgrade, so on the deploy day it catches up
        // like any brand-new reminder (ReminderDefaultsMatchSeedMigrationTests).
        new() { Id = "timesheet-before-leave", Enabled = true, Time = "14:00", Frequency = "daily" },
    };

    public static readonly IReadOnlySet<string> KnownIds =
        new HashSet<string>(Create().ConvertAll(r => r.Id));
}
