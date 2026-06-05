namespace Infrastructure.Configuration;

public class MailSettings
{
    public const string SectionName = "MailSettings";

    // SMTP login username (e.g. Brevo SMTP login, or the literal "apikey" for SendGrid).
    public string Mail { get; set; } = string.Empty;

    // Address shown in the From header (e.g. "noreply@jenus.com.cy"). Falls back to Mail when empty.
    public string FromAddress { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;

    // Effective From address: explicit FromAddress, otherwise the SMTP login.
    public string EffectiveFromAddress =>
        string.IsNullOrWhiteSpace(FromAddress) ? Mail : FromAddress;
}
