namespace API.Hubs;

/// <summary>
/// What a <c>notificationsUpdated</c> event says changed, sent as its one argument
/// so a client refetches only what it touches (<c>client/src/lib/notification-kinds.ts</c>
/// maps each to its query keys — keep the two in step). An event with no kind still
/// means "refresh everything" there, which is what every event used to mean.
/// </summary>
public static class NotificationKind
{
    public const string Leave = "leave";
    public const string Timesheet = "timesheet";
    public const string Task = "task";
}
