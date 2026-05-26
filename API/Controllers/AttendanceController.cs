using System.Security.Claims;
using Application.Attendance.DTOs;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttendanceController : ControllerBase
{
    private readonly AppDbContext _context;

    public AttendanceController(AppDbContext context)
    {
        _context = context;
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? User.Identity?.Name
        ?? string.Empty;

    private async Task<EmployeeProfile?> GetEmployeeProfileAsync(string userId)
    {
        return await _context.EmployeeProfiles.FirstOrDefaultAsync(ep => ep.UserId == userId);
    }

    private static DateTime UtcDayStart(DateTime dt) =>
        new(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime AsUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    private static DateTime? AsUtcNullable(DateTime? dt) =>
        dt.HasValue ? AsUtc(dt.Value) : null;

    private static string EventTypeName(AttendanceEventType type) => type switch
    {
        AttendanceEventType.CheckIn => "check-in",
        AttendanceEventType.CheckOut => "check-out",
        AttendanceEventType.BreakStart => "break-start",
        AttendanceEventType.BreakEnd => "break-end",
        _ => type.ToString().ToLowerInvariant()
    };

    private static (string status, DateTime? checkInAt, DateTime? checkOutAt, DateTime? onBreakSince, int totalBreakMinutes, int workedMinutes)
        ComputeDayState(List<AttendanceEvent> dayEvents, DateTime nowUtc)
    {
        var ordered = dayEvents.OrderBy(e => e.At).ToList();

        DateTime? checkIn = null;
        DateTime? checkOut = null;
        DateTime? breakStart = null;
        int totalBreakSeconds = 0;

        foreach (var e in ordered)
        {
            switch (e.Type)
            {
                case AttendanceEventType.CheckIn:
                    if (checkIn == null) checkIn = e.At;
                    break;
                case AttendanceEventType.CheckOut:
                    checkOut = e.At;
                    if (breakStart != null)
                    {
                        totalBreakSeconds += (int)(e.At - breakStart.Value).TotalSeconds;
                        breakStart = null;
                    }
                    break;
                case AttendanceEventType.BreakStart:
                    if (checkIn != null && checkOut == null && breakStart == null)
                        breakStart = e.At;
                    break;
                case AttendanceEventType.BreakEnd:
                    if (breakStart != null)
                    {
                        totalBreakSeconds += (int)(e.At - breakStart.Value).TotalSeconds;
                        breakStart = null;
                    }
                    break;
            }
        }

        string status;
        if (checkIn == null) status = "out";
        else if (checkOut != null) status = "done";
        else if (breakStart != null) status = "break";
        else status = "in";

        int workedMinutes = 0;
        if (checkIn != null)
        {
            var end = checkOut ?? nowUtc;
            var totalSeconds = (int)(end - checkIn.Value).TotalSeconds;
            var openBreakSeconds = breakStart != null && checkOut == null
                ? (int)(nowUtc - breakStart.Value).TotalSeconds
                : 0;
            workedMinutes = Math.Max(0, (totalSeconds - totalBreakSeconds - openBreakSeconds) / 60);
        }

        return (status, checkIn, checkOut, breakStart, totalBreakSeconds / 60, workedMinutes);
    }

    private async Task<TodayStateDto> BuildTodayStateAsync(string employeeId)
    {
        var now = DateTime.UtcNow;
        var dayStart = UtcDayStart(now);
        var dayEnd = dayStart.AddDays(1);

        var events = await _context.AttendanceEvents
            .Where(e => e.EmployeeId == employeeId && e.At >= dayStart && e.At < dayEnd)
            .OrderBy(e => e.At)
            .ToListAsync();

        var s = ComputeDayState(events, now);

        var dtoEvents = events
            .Select(e => new AttendanceEventDto(e.Id, AsUtc(e.At), EventTypeName(e.Type)))
            .ToList();

        return new TodayStateDto(
            dayStart.ToString("yyyy-MM-dd"),
            s.status,
            AsUtcNullable(s.checkInAt),
            AsUtcNullable(s.checkOutAt),
            AsUtcNullable(s.onBreakSince),
            s.totalBreakMinutes,
            s.workedMinutes,
            dtoEvents);
    }

    [HttpGet("me/today")]
    public async Task<ActionResult<TodayStateDto>> GetToday()
    {
        var userId = GetUserId();
        var profile = await GetEmployeeProfileAsync(userId);
        if (profile == null) return BadRequest("No employee profile found.");
        return Ok(await BuildTodayStateAsync(profile.Id));
    }

    [HttpPost("check-in")]
    public async Task<ActionResult<TodayStateDto>> CheckIn()
    {
        var userId = GetUserId();
        var profile = await GetEmployeeProfileAsync(userId);
        if (profile == null) return BadRequest("No employee profile found.");

        var now = DateTime.UtcNow;
        var dayStart = UtcDayStart(now);
        var dayEnd = dayStart.AddDays(1);

        var existing = await _context.AttendanceEvents
            .Where(e => e.EmployeeId == profile.Id && e.At >= dayStart && e.At < dayEnd)
            .OrderBy(e => e.At)
            .ToListAsync();

        var s = ComputeDayState(existing, now);
        if (s.status == "in" || s.status == "break")
            return Conflict(new { error = "Already checked in." });

        _context.AttendanceEvents.Add(new AttendanceEvent
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = profile.Id,
            At = now,
            Type = AttendanceEventType.CheckIn,
        });
        await _context.SaveChangesAsync();
        return Ok(await BuildTodayStateAsync(profile.Id));
    }

    [HttpPost("check-out")]
    public async Task<ActionResult<TodayStateDto>> CheckOut()
    {
        var userId = GetUserId();
        var profile = await GetEmployeeProfileAsync(userId);
        if (profile == null) return BadRequest("No employee profile found.");

        var now = DateTime.UtcNow;
        var dayStart = UtcDayStart(now);
        var dayEnd = dayStart.AddDays(1);

        var existing = await _context.AttendanceEvents
            .Where(e => e.EmployeeId == profile.Id && e.At >= dayStart && e.At < dayEnd)
            .OrderBy(e => e.At)
            .ToListAsync();

        var s = ComputeDayState(existing, now);
        if (s.status != "in" && s.status != "break")
            return Conflict(new { error = "Not currently checked in." });

        if (s.status == "break")
        {
            _context.AttendanceEvents.Add(new AttendanceEvent
            {
                Id = Guid.NewGuid().ToString(),
                EmployeeId = profile.Id,
                At = now,
                Type = AttendanceEventType.BreakEnd,
            });
        }
        _context.AttendanceEvents.Add(new AttendanceEvent
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = profile.Id,
            At = now,
            Type = AttendanceEventType.CheckOut,
        });
        await _context.SaveChangesAsync();
        return Ok(await BuildTodayStateAsync(profile.Id));
    }

    [HttpPost("break/start")]
    public async Task<ActionResult<TodayStateDto>> StartBreak()
    {
        var userId = GetUserId();
        var profile = await GetEmployeeProfileAsync(userId);
        if (profile == null) return BadRequest("No employee profile found.");

        var now = DateTime.UtcNow;
        var dayStart = UtcDayStart(now);
        var dayEnd = dayStart.AddDays(1);

        var existing = await _context.AttendanceEvents
            .Where(e => e.EmployeeId == profile.Id && e.At >= dayStart && e.At < dayEnd)
            .OrderBy(e => e.At)
            .ToListAsync();

        var s = ComputeDayState(existing, now);
        if (s.status != "in")
            return Conflict(new { error = "Can only start a break while working." });

        _context.AttendanceEvents.Add(new AttendanceEvent
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = profile.Id,
            At = now,
            Type = AttendanceEventType.BreakStart,
        });
        await _context.SaveChangesAsync();
        return Ok(await BuildTodayStateAsync(profile.Id));
    }

    [HttpPost("break/end")]
    public async Task<ActionResult<TodayStateDto>> EndBreak()
    {
        var userId = GetUserId();
        var profile = await GetEmployeeProfileAsync(userId);
        if (profile == null) return BadRequest("No employee profile found.");

        var now = DateTime.UtcNow;
        var dayStart = UtcDayStart(now);
        var dayEnd = dayStart.AddDays(1);

        var existing = await _context.AttendanceEvents
            .Where(e => e.EmployeeId == profile.Id && e.At >= dayStart && e.At < dayEnd)
            .OrderBy(e => e.At)
            .ToListAsync();

        var s = ComputeDayState(existing, now);
        if (s.status != "break")
            return Conflict(new { error = "Not currently on break." });

        _context.AttendanceEvents.Add(new AttendanceEvent
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeId = profile.Id,
            At = now,
            Type = AttendanceEventType.BreakEnd,
        });
        await _context.SaveChangesAsync();
        return Ok(await BuildTodayStateAsync(profile.Id));
    }

    [HttpGet("me/history")]
    public async Task<ActionResult<List<DayHistoryDto>>> GetMyHistory([FromQuery] int days = 30)
    {
        if (days <= 0 || days > 180) days = 30;

        var userId = GetUserId();
        var profile = await GetEmployeeProfileAsync(userId);
        if (profile == null) return BadRequest("No employee profile found.");

        var now = DateTime.UtcNow;
        var from = UtcDayStart(now).AddDays(-(days - 1));

        var events = await _context.AttendanceEvents
            .Where(e => e.EmployeeId == profile.Id && e.At >= from)
            .OrderBy(e => e.At)
            .ToListAsync();

        var byDay = events.GroupBy(e => UtcDayStart(e.At)).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<DayHistoryDto>();
        for (int i = days - 1; i >= 0; i--)
        {
            var date = UtcDayStart(now).AddDays(-i);
            byDay.TryGetValue(date, out var dayEvents);
            var s = ComputeDayState(dayEvents ?? new List<AttendanceEvent>(), now);
            var status = s.status switch
            {
                "out" => "absent",
                "in" => "in-progress",
                "break" => "in-progress",
                "done" => (s.checkInAt.HasValue && s.checkInAt.Value.Hour > 9 ? "late" : "complete"),
                _ => s.status,
            };
            result.Add(new DayHistoryDto(
                date.ToString("yyyy-MM-dd"),
                status,
                AsUtcNullable(s.checkInAt),
                AsUtcNullable(s.checkOutAt),
                s.totalBreakMinutes,
                s.workedMinutes));
        }
        return Ok(result);
    }

    [HttpGet("team")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<TeamAttendanceDto>> GetTeam()
    {
        var userId = GetUserId();
        var isAdmin = User.IsInRole("Admin");

        var profilesQuery = _context.EmployeeProfiles
            .Include(p => p.User)
            .Include(p => p.Department)
            .AsQueryable();

        if (!isAdmin)
        {
            var me = await GetEmployeeProfileAsync(userId);
            if (me == null) return BadRequest("No employee profile found.");
            profilesQuery = profilesQuery.Where(p => p.ManagerId == me.Id);
        }

        var profiles = await profilesQuery.OrderBy(p => p.User != null ? p.User.DisplayName : "").ToListAsync();
        var employeeIds = profiles.Select(p => p.Id).ToList();

        var now = DateTime.UtcNow;
        var todayStart = UtcDayStart(now);
        var todayEnd = todayStart.AddDays(1);

        var todayEvents = await _context.AttendanceEvents
            .Where(e => employeeIds.Contains(e.EmployeeId) && e.At >= todayStart && e.At < todayEnd)
            .ToListAsync();
        var todayByEmployee = todayEvents.GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        // Today's leaves (Approved)
        var leavesToday = await _context.AnnualLeaves
            .Where(l => employeeIds.Contains(l.EmployeeId)
                && l.Status == AnnualLeaveStatus.Approved
                && l.StartDate <= now && l.EndDate >= now)
            .Select(l => l.EmployeeId)
            .ToListAsync();
        var onLeaveSet = new HashSet<string>(leavesToday);

        var members = new List<TeamMemberAttendanceDto>();
        foreach (var p in profiles)
        {
            todayByEmployee.TryGetValue(p.Id, out var evts);
            var s = ComputeDayState(evts ?? new List<AttendanceEvent>(), now);

            string status;
            string note;
            if (onLeaveSet.Contains(p.Id)) { status = "leave"; note = "On leave today"; }
            else if (s.status == "in") { status = "in"; note = s.checkInAt!.Value.Hour >= 10 ? "Late check-in" : "On track"; }
            else if (s.status == "break") { status = "break"; note = "On break"; }
            else if (s.status == "done") { status = "out"; note = $"Done at {s.checkOutAt:HH:mm}"; }
            else { status = "out"; note = "Not checked in"; }

            members.Add(new TeamMemberAttendanceDto(
                p.Id,
                p.User?.DisplayName ?? p.User?.UserName ?? "Unknown",
                p.Department?.Name ?? "",
                p.JobTitle,
                status,
                AsUtcNullable(s.checkInAt),
                s.workedMinutes,
                AsUtcNullable(s.onBreakSince),
                note));
        }

        // Week table (Mon..Fri of current ISO week, UTC)
        int diffToMonday = (int)now.DayOfWeek - (int)DayOfWeek.Monday;
        if (diffToMonday < 0) diffToMonday += 7;
        var monday = UtcDayStart(now).AddDays(-diffToMonday);
        var friday = monday.AddDays(5);

        var weekEvents = await _context.AttendanceEvents
            .Where(e => employeeIds.Contains(e.EmployeeId) && e.At >= monday && e.At < friday)
            .ToListAsync();

        var weekRows = new List<TeamWeekRowDto>();
        foreach (var p in profiles)
        {
            var days = new List<WeekDayHoursDto>();
            int total = 0;
            for (int i = 0; i < 5; i++)
            {
                var dayStart = monday.AddDays(i);
                var dayEnd = dayStart.AddDays(1);
                var evts = weekEvents
                    .Where(e => e.EmployeeId == p.Id && e.At >= dayStart && e.At < dayEnd)
                    .ToList();
                var s = ComputeDayState(evts, now);
                int? minutes = s.checkInAt == null ? (int?)null : s.workedMinutes;
                string? note = s.status switch
                {
                    "in" => "in",
                    "break" => "break",
                    "done" => null,
                    _ => null,
                };
                if (minutes != null) total += minutes.Value;
                days.Add(new WeekDayHoursDto(dayStart.ToString("yyyy-MM-dd"), minutes, note));
            }
            weekRows.Add(new TeamWeekRowDto(p.Id, p.User?.DisplayName ?? p.User?.UserName ?? "Unknown", days, total));
        }

        return Ok(new TeamAttendanceDto(members, weekRows));
    }

    [HttpGet("company")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CompanyAttendanceDto>> GetCompany()
    {
        var now = DateTime.UtcNow;
        var todayStart = UtcDayStart(now);
        var todayEnd = todayStart.AddDays(1);

        var profiles = await _context.EmployeeProfiles
            .Include(p => p.User)
            .Include(p => p.Department)
            .ToListAsync();

        var employeeIds = profiles.Select(p => p.Id).ToList();

        var todayEvents = await _context.AttendanceEvents
            .Where(e => employeeIds.Contains(e.EmployeeId) && e.At >= todayStart && e.At < todayEnd)
            .ToListAsync();
        var todayByEmployee = todayEvents.GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var leavesToday = await _context.AnnualLeaves
            .Where(l => employeeIds.Contains(l.EmployeeId)
                && l.Status == AnnualLeaveStatus.Approved
                && l.StartDate <= now && l.EndDate >= now)
            .Select(l => l.EmployeeId)
            .ToListAsync();
        var onLeaveSet = new HashSet<string>(leavesToday);

        // Per-department aggregation
        var deptGroups = profiles
            .GroupBy(p => p.Department?.Name ?? "Unassigned")
            .OrderBy(g => g.Key);

        var departments = new List<DeptAttendanceDto>();
        int totalCount = 0, inCount = 0, breakCount = 0, outCount = 0, leaveCount = 0;
        int totalMinutesAll = 0;

        foreach (var grp in deptGroups)
        {
            int dIn = 0, dBreak = 0, dOut = 0, dLeave = 0, dMinutes = 0;
            int dWorkedPeople = 0;
            foreach (var p in grp)
            {
                if (onLeaveSet.Contains(p.Id)) { dLeave++; continue; }
                todayByEmployee.TryGetValue(p.Id, out var evts);
                var s = ComputeDayState(evts ?? new List<AttendanceEvent>(), now);
                if (s.status == "in") dIn++;
                else if (s.status == "break") dBreak++;
                else dOut++;
                if (s.workedMinutes > 0) { dMinutes += s.workedMinutes; dWorkedPeople++; }
            }
            departments.Add(new DeptAttendanceDto(
                grp.Key,
                grp.Count(),
                dIn,
                dBreak,
                dOut,
                dLeave,
                dMinutes,
                dWorkedPeople > 0 ? dMinutes / dWorkedPeople : 0));
            totalCount += grp.Count();
            inCount += dIn;
            breakCount += dBreak;
            outCount += dOut;
            leaveCount += dLeave;
            totalMinutesAll += dMinutes;
        }
        int workedPeopleAll = profiles.Count(p =>
        {
            todayByEmployee.TryGetValue(p.Id, out var evts);
            return ComputeDayState(evts ?? new List<AttendanceEvent>(), now).workedMinutes > 0;
        });
        int avgMinutesAll = workedPeopleAll > 0 ? totalMinutesAll / workedPeopleAll : 0;

        // Recent activity (last 20 events today)
        var recentEvents = await _context.AttendanceEvents
            .Where(e => employeeIds.Contains(e.EmployeeId) && e.At >= todayStart && e.At < todayEnd)
            .OrderByDescending(e => e.At)
            .Take(20)
            .ToListAsync();

        var profileById = profiles.ToDictionary(p => p.Id);
        var recentDto = recentEvents.Select(e =>
        {
            profileById.TryGetValue(e.EmployeeId, out var prof);
            var ago = (int)Math.Max(0, (now - e.At).TotalMinutes);
            string action;
            if (e.Type == AttendanceEventType.CheckIn)
            {
                action = AsUtc(e.At).Hour >= 10 ? "Late check-in" : "Checked in";
            }
            else if (e.Type == AttendanceEventType.CheckOut) action = "Checked out";
            else if (e.Type == AttendanceEventType.BreakStart) action = "Started break";
            else action = "Back from break";

            return new RecentActivityDto(
                prof?.User?.DisplayName ?? prof?.User?.UserName ?? "Unknown",
                prof?.Department?.Name ?? "Unassigned",
                action,
                AsUtc(e.At),
                ago);
        }).ToList();

        // Add "Not checked in" entries (no events today, not on leave) — flagged after 10:00 UTC
        if (now.Hour >= 10)
        {
            var notChecked = profiles
                .Where(p => !onLeaveSet.Contains(p.Id) && !todayByEmployee.ContainsKey(p.Id))
                .Take(5)
                .Select(p => new RecentActivityDto(
                    p.User?.DisplayName ?? p.User?.UserName ?? "Unknown",
                    p.Department?.Name ?? "Unassigned",
                    "Not checked in",
                    null,
                    null));
            recentDto = recentDto.Concat(notChecked).ToList();
        }

        // Issues
        var issues = new List<IssueDto>();

        // 1) Departments with not-checked-in counts (flagged after 10:00)
        if (now.Hour >= 10)
        {
            foreach (var dept in departments.Where(d => d.Out > 0))
            {
                issues.Add(new IssueDto(
                    "danger",
                    $"{dept.Out} not checked in ({dept.Name})",
                    $"No check-in by {now.Hour:D2}:00 · likely unscheduled absence"));
            }
        }

        // 2) Late check-ins
        var lateNames = new List<string>();
        foreach (var p in profiles)
        {
            if (onLeaveSet.Contains(p.Id)) continue;
            todayByEmployee.TryGetValue(p.Id, out var evts);
            var s = ComputeDayState(evts ?? new List<AttendanceEvent>(), now);
            if (s.checkInAt.HasValue && AsUtc(s.checkInAt.Value).Hour >= 10)
            {
                var name = p.User?.DisplayName ?? p.User?.UserName ?? "Unknown";
                var dept = p.Department?.Name ?? "Unassigned";
                var lateMin = (int)(s.checkInAt.Value - todayStart.AddHours(9)).TotalMinutes;
                lateNames.Add($"{name} ({dept}) · {lateMin} min late");
            }
        }
        if (lateNames.Count > 0)
        {
            issues.Add(new IssueDto(
                "warning",
                $"{lateNames.Count} late check-in{(lateNames.Count == 1 ? "" : "s")}",
                string.Join(" · ", lateNames.Take(3))));
        }

        // 3) On leave summary
        if (leaveCount > 0)
        {
            var deptBreakdown = onLeaveSet
                .Select(id => profileById.TryGetValue(id, out var p) ? p.Department?.Name ?? "Unassigned" : "Unassigned")
                .GroupBy(name => name)
                .Select(g => $"{g.Count()} {g.Key}")
                .ToList();
            issues.Add(new IssueDto(
                "info",
                $"{leaveCount} on approved leave",
                string.Join(" · ", deptBreakdown)));
        }

        // 4) Overtime sanity (anyone over 10h today)
        var overtime = profiles
            .Where(p =>
            {
                todayByEmployee.TryGetValue(p.Id, out var evts);
                return ComputeDayState(evts ?? new List<AttendanceEvent>(), now).workedMinutes > 600;
            })
            .Count();
        if (overtime == 0)
        {
            issues.Add(new IssueDto(
                "success",
                "No unusual overtime",
                "All employees within healthy hour ranges"));
        }
        else
        {
            issues.Add(new IssueDto(
                "warning",
                $"{overtime} over 10 hours today",
                "Consider checking in"));
        }

        return Ok(new CompanyAttendanceDto(
            totalCount,
            inCount,
            breakCount,
            outCount,
            leaveCount,
            totalMinutesAll,
            avgMinutesAll,
            departments,
            recentDto,
            issues));
    }
}
