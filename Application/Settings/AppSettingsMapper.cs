using Application.Settings.DTOs;

namespace Application.Settings;

public static class AppSettingsMapper
{
    public static AppSettingsDto ToDto(Domain.AppSettings s) => new()
    {
        LeaveYearStartMonth = s.LeaveYearStartMonth,
        MaxCarryoverDays = s.MaxCarryoverDays,
        DefaultAnnualEntitlement = s.DefaultAnnualEntitlement,
        YearEndWarningDays = s.YearEndWarningDays,
        FinalWarningDays = s.FinalWarningDays,
        AutoRunRollover = s.AutoRunRollover,
        SendYearEndWarningEmails = s.SendYearEndWarningEmails,
        BlockLeaveSpanningIntoNextYear = s.BlockLeaveSpanningIntoNextYear,
        NotifyManagersOfTeamExpiries = s.NotifyManagersOfTeamExpiries,
        HolidayCountryCode = s.HolidayCountryCode,
        HolidayCountryName = s.HolidayCountryName,
        WorkingHoursStart = s.WorkingHoursStart,
        WorkingHoursEnd = s.WorkingHoursEnd,
        TimeZoneId = s.TimeZoneId,
        FinancialYearStartMonth = s.FinancialYearStartMonth,
        WorkingDays = s.WorkingDays,
        WorkingDaysCustom = s.WorkingDaysCustom,
        EmailNotificationsEnabled = s.EmailNotificationsEnabled,
        EmailDailyDigest = s.EmailDailyDigest,
        EmailUrgentOnly = s.EmailUrgentOnly,
        SlackEnabled = s.SlackEnabled,
        Reminders = ReminderSerializer.FromJson(s.RemindersJson),
        // SlackConnected is populated by the API layer (webhook config lives in
        // Infrastructure, not the DB).
    };
}
