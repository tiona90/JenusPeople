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
            if (request.LeaveYearStartMonth < 1 || request.LeaveYearStartMonth > 12)
                return Result<AppSettingsDto>.Failure("Leave year start month must be between 1 and 12.");
            if (request.MaxCarryoverDays < 0)
                return Result<AppSettingsDto>.Failure("Max carryover days cannot be negative.");
            if (request.DefaultAnnualEntitlement < 1)
                return Result<AppSettingsDto>.Failure("Default annual entitlement must be at least 1.");
            if (request.FinancialYearStartMonth < 1 || request.FinancialYearStartMonth > 12)
                return Result<AppSettingsDto>.Failure("Financial year start month must be between 1 and 12.");
            if (!TryNormalizeTime(request.WorkingHoursStart, out var workStart))
                return Result<AppSettingsDto>.Failure("Working hours start must be a valid time (HH:mm).");
            if (!TryNormalizeTime(request.WorkingHoursEnd, out var workEnd))
                return Result<AppSettingsDto>.Failure("Working hours end must be a valid time (HH:mm).");

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

        // Accepts "H:mm"/"HH:mm"; emits canonical "HH:mm".
        private static bool TryNormalizeTime(string? value, out string normalized)
        {
            if (TimeOnly.TryParse(value, out var t))
            {
                normalized = t.ToString("HH:mm");
                return true;
            }
            normalized = "00:00";
            return false;
        }
    }
}
