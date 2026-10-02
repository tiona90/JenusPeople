namespace Domain;

/// <summary>
/// The last day each reminder on Notification Settings was handled, one row per
/// reminder id. It is what stops a reminder going out twice in a day: the
/// scheduler used to remember this in memory alone, so every restart of the API
/// — a deploy, an app-pool recycle, IIS waking the site for its first visitor —
/// forgot the morning's sends and repeated them (fifteen copies of the
/// pending-approvals digest on one development day).
///
/// <see cref="RanOn"/> is the date on the org's clock (<c>AppSettings.TimeZoneId</c>),
/// the same date the schedule is evaluated against. The row is written
/// <em>before</em> the reminder is dispatched, so a process that dies mid-send
/// does not send again when it comes back; <see cref="Outcome"/> then still
/// reads "dispatching", which is the clue.
/// </summary>
public class ReminderRun
{
    public const int ReminderIdMaxLength = 64;
    public const int OutcomeMaxLength = 300;

    /// <summary>The reminder's catalogue id (<c>ReminderDefaults</c>): <c>check-in</c>, <c>pending-approvals</c>, …</summary>
    public string ReminderId { get; set; } = string.Empty;

    /// <summary>The org-local date the reminder was last handled — sent, or deliberately skipped.</summary>
    public DateOnly RanOn { get; set; }

    /// <summary>When that happened.</summary>
    public DateTime RanAtUtc { get; set; }

    /// <summary>What happened: <c>dispatching</c>, <c>sent</c>, <c>skipped: …</c> or <c>failed: …</c>.</summary>
    public string Outcome { get; set; } = string.Empty;
}
