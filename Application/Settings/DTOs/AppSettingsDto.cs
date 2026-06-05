namespace Application.Settings.DTOs;

public class AppSettingsDto
{

    public int LeaveYearStartMonth { get; set; }
    public int MaxCarryoverDays { get; set; }
    public int DefaultAnnualEntitlement { get; set; }
    public int YearEndWarningDays { get; set; }
    public int FinalWarningDays { get; set; }
    public bool AutoRunRollover { get; set; }
    public bool SendYearEndWarningEmails { get; set; }
    public bool BlockLeaveSpanningIntoNextYear { get; set; }
    public bool NotifyManagersOfTeamExpiries { get; set; }
    public string? HolidayCountryCode { get; set; }
    public string? HolidayCountryName { get; set; }

    // ── Organization settings ──────────────────────────────────────────────
    public string WorkingHoursStart { get; set; } = "09:00";
    public string WorkingHoursEnd { get; set; } = "18:00";
    public string TimeZoneId { get; set; } = "UTC";
    public int FinancialYearStartMonth { get; set; } = 1;
    public string WorkingDays { get; set; } = "mon-fri";

    // ── Email notification preferences ─────────────────────────────────────
    public bool EmailNotificationsEnabled { get; set; } = true;
    public bool EmailDailyDigest { get; set; } = true;
    public bool EmailUrgentOnly { get; set; }

    // ── Slack integration ──────────────────────────────────────────────────
    public bool SlackEnabled { get; set; }
    // Computed (not stored): whether a webhook is configured server-side.
    public bool SlackConnected { get; set; }

    // ── Reminders ──────────────────────────────────────────────────────────
    public List<ReminderSettingDto> Reminders { get; set; } = new();
}
