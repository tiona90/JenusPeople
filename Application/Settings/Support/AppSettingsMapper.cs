using Application.Settings.DTOs;

namespace Application.Settings.Support;

public static class AppSettingsMapper
{
    public static AppSettingsDto ToDto(Domain.AppSettings s) => new()
    {
        LeaveYearStartMonth = s.LeaveYearStartMonth,
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
        WeeklyHoursTarget = s.WeeklyHoursTarget,
        TimesheetSubmissionDeadlineDay = s.TimesheetSubmissionDeadlineDay,
        TimesheetSubmissionDeadlineTime = s.TimesheetSubmissionDeadlineTime,
        EmailNotificationsEnabled = s.EmailNotificationsEnabled,
        EmailDailyDigest = s.EmailDailyDigest,
        EmailUrgentOnly = s.EmailUrgentOnly,
        Reminders = ReminderSerializer.FromJson(s.RemindersJson),
    };
}
