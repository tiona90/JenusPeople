using Application.Core;
using Application.Settings.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Settings.Commands;

public class UpdateAppSettings
{
    public class Command : IRequest<Result<AppSettingsDto>>
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

        // Organization
        public string WorkingHoursStart { get; set; } = "09:00";
        public string WorkingHoursEnd { get; set; } = "18:00";
        public string TimeZoneId { get; set; } = "UTC";
        public int FinancialYearStartMonth { get; set; } = 1;
        public string WorkingDays { get; set; } = "mon-fri";
        public string WorkingDaysCustom { get; set; } = "mon,tue,wed,thu,fri";

        // Timesheet policy
        public int WeeklyHoursTarget { get; set; } = 40;
        public string TimesheetSubmissionDeadlineDay { get; set; } = "fri";
        public string TimesheetSubmissionDeadlineTime { get; set; } = "18:00";

        // Email
        public bool EmailNotificationsEnabled { get; set; } = true;
        public bool EmailDailyDigest { get; set; } = true;
        public bool EmailUrgentOnly { get; set; }

        // Slack
        public bool SlackEnabled { get; set; }

        // Reminders
        public List<ReminderSettingDto> Reminders { get; set; } = new();
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<AppSettingsDto>>
    {
        public async Task<Result<AppSettingsDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            // UpdateAppSettingsValidator runs ahead of this handler and rejects all
            // of the below, so anything arriving through the API has already been
            // checked. These stay as a backstop for a direct call, and now report a
            // validation failure (400, naming the field) instead of Result.Failure,
            // which maps to 404 — the wrong answer for a malformed request.
            if (request.LeaveYearStartMonth < 1 || request.LeaveYearStartMonth > 12)
                return Invalid(nameof(request.LeaveYearStartMonth), "Leave year start month must be between 1 and 12.");
            if (request.MaxCarryoverDays < 0)
                return Invalid(nameof(request.MaxCarryoverDays), "Max carryover days cannot be negative.");
            if (request.DefaultAnnualEntitlement < 1)
                return Invalid(nameof(request.DefaultAnnualEntitlement), "Default annual entitlement must be at least 1.");
            if (request.FinancialYearStartMonth < 1 || request.FinancialYearStartMonth > 12)
                return Invalid(nameof(request.FinancialYearStartMonth), "Financial year start month must be between 1 and 12.");
            if (!WorkingTimeFormat.TryNormalizeTime(request.WorkingHoursStart, out var workStart))
                return Invalid(nameof(request.WorkingHoursStart), "Working hours start must be a valid time (HH:mm).");
            if (!WorkingTimeFormat.TryNormalizeTime(request.WorkingHoursEnd, out var workEnd))
                return Invalid(nameof(request.WorkingHoursEnd), "Working hours end must be a valid time (HH:mm).");
            if (request.WeeklyHoursTarget < 1 || request.WeeklyHoursTarget > 168)
                return Invalid(nameof(request.WeeklyHoursTarget), "Weekly hours target must be between 1 and 168.");
            if (!WorkingTimeFormat.IsKnownDay(request.TimesheetSubmissionDeadlineDay))
                return Invalid(nameof(request.TimesheetSubmissionDeadlineDay), "Timesheet submission deadline day must be a weekday (mon–sun).");
            if (!WorkingTimeFormat.TryNormalizeTime(request.TimesheetSubmissionDeadlineTime, out var deadlineTime))
                return Invalid(nameof(request.TimesheetSubmissionDeadlineTime), "Timesheet submission deadline time must be a valid time (HH:mm).");

            var settings = await context.AppSettings.FirstOrDefaultAsync(cancellationToken);
            if (settings is null)
            {
                settings = new Domain.AppSettings();
                context.AppSettings.Add(settings);
            }

            settings.LeaveYearStartMonth = request.LeaveYearStartMonth;
            settings.MaxCarryoverDays = request.MaxCarryoverDays;
            settings.DefaultAnnualEntitlement = request.DefaultAnnualEntitlement;
            settings.YearEndWarningDays = request.YearEndWarningDays;
            settings.FinalWarningDays = request.FinalWarningDays;
            settings.AutoRunRollover = request.AutoRunRollover;
            settings.SendYearEndWarningEmails = request.SendYearEndWarningEmails;
            settings.BlockLeaveSpanningIntoNextYear = request.BlockLeaveSpanningIntoNextYear;
            settings.NotifyManagersOfTeamExpiries = request.NotifyManagersOfTeamExpiries;

            settings.WorkingHoursStart = workStart;
            settings.WorkingHoursEnd = workEnd;
            settings.TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "UTC" : request.TimeZoneId.Trim();
            settings.FinancialYearStartMonth = request.FinancialYearStartMonth;
            settings.WorkingDays = string.IsNullOrWhiteSpace(request.WorkingDays) ? "mon-fri" : request.WorkingDays.Trim();
            settings.WorkingDaysCustom = WorkingTimeFormat.NormalizeWorkingDaysCustom(request.WorkingDaysCustom);
            if (settings.WorkingDays == "custom" && settings.WorkingDaysCustom.Length == 0)
                return Invalid(nameof(request.WorkingDaysCustom), "Select at least one working day for the custom schedule.");
            settings.WeeklyHoursTarget = request.WeeklyHoursTarget;
            settings.TimesheetSubmissionDeadlineDay = request.TimesheetSubmissionDeadlineDay!.Trim().ToLowerInvariant();
            settings.TimesheetSubmissionDeadlineTime = deadlineTime;
            settings.EmailNotificationsEnabled = request.EmailNotificationsEnabled;
            settings.EmailDailyDigest = request.EmailDailyDigest;
            settings.EmailUrgentOnly = request.EmailUrgentOnly;
            settings.SlackEnabled = request.SlackEnabled;
            settings.RemindersJson = ReminderSerializer.ToJson(request.Reminders);

            var newCode = request.HolidayCountryCode?.Trim().ToUpperInvariant();
            var countryChanged = !string.Equals(settings.HolidayCountryCode, newCode, StringComparison.OrdinalIgnoreCase);
            settings.HolidayCountryCode = string.IsNullOrEmpty(newCode) ? null : newCode;
            settings.HolidayCountryName = string.IsNullOrWhiteSpace(request.HolidayCountryName) ? null : request.HolidayCountryName.Trim();

            // Country changed → invalidate cached holidays from the previous country.
            if (countryChanged)
            {
                var stale = await context.PublicHolidays.ToListAsync(cancellationToken);
                if (stale.Count > 0) context.PublicHolidays.RemoveRange(stale);
            }

            await context.SaveChangesAsync(cancellationToken);

            return Result<AppSettingsDto>.Success(AppSettingsMapper.ToDto(settings));
        }

        // Parsing and day-name rules live in WorkingTimeFormat, shared with
        // UpdateAppSettingsValidator so the two agree on what a valid value is.
        private static Result<AppSettingsDto> Invalid(string field, string message) =>
            Result<AppSettingsDto>.ValidationFailure(
                new Dictionary<string, string[]> { [field] = [message] },
                message);
    }
}
