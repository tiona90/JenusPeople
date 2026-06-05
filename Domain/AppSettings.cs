namespace Domain;

public class AppSettings
{
    public int Id { get; set; }

    public int LeaveYearStartMonth { get; set; } = 1;
    public int MaxCarryoverDays { get; set; } = 5;
    public int DefaultAnnualEntitlement { get; set; } = 20;
    public int YearEndWarningDays { get; set; } = 30;
    public int FinalWarningDays { get; set; } = 7;
    public bool AutoRunRollover { get; set; } = true;
    public bool SendYearEndWarningEmails { get; set; } = true;
    public bool BlockLeaveSpanningIntoNextYear { get; set; } = true;
    public bool NotifyManagersOfTeamExpiries { get; set; } = true;

    public string? HolidayCountryCode { get; set; }
    public string? HolidayCountryName { get; set; }

    // ── Organization settings ──────────────────────────────────────────────
    // Stored as "HH:mm" strings (display/config only — no scheduler consumes
    // them yet). Serve as the org-wide defaults for attendance/check-in.
    public string WorkingHoursStart { get; set; } = "09:00";
    public string WorkingHoursEnd { get; set; } = "18:00";
    public string TimeZoneId { get; set; } = "UTC";
    public int FinancialYearStartMonth { get; set; } = 1;
    // "mon-fri" | "mon-sat" | "sun-fri" | "custom"
    public string WorkingDays { get; set; } = "mon-fri";

    // ── Email notification preferences ─────────────────────────────────────
    public bool EmailNotificationsEnabled { get; set; } = true;
    public bool EmailDailyDigest { get; set; } = true;
    public bool EmailUrgentOnly { get; set; }

    // ── Slack integration ──────────────────────────────────────────────────
    // User preference to post reminders to Slack. The webhook URL itself is
    // configured server-side (Infrastructure.Configuration.SlackOptions); this
    // flag only controls whether enabled flows fan out to it.
    public bool SlackEnabled { get; set; }

    // ── Reminders ──────────────────────────────────────────────────────────
    // Serialized JSON list of ReminderSetting (id/enabled/time/frequency).
    // Empty string means "use defaults". Kept as a single column because the
    // settings row is a singleton and the shape is small and read together.
    public string RemindersJson { get; set; } = string.Empty;
}
