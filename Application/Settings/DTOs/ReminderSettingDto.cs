namespace Application.Settings.DTOs;

// Configurable state for one reminder type. The display metadata (emoji,
// name, description, preview copy) lives in the client — the backend only
// persists what the admin can change.
public class ReminderSettingDto
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Time { get; set; } = "09:00";      // "HH:mm"
    public string Frequency { get; set; } = "daily";  // "daily" | "weekly"
}
