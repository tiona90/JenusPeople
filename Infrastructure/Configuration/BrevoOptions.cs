namespace Infrastructure.Configuration;

public class BrevoOptions
{
    public const string SectionName = "Brevo";

    // Brevo transactional email API base URL.
    public string BaseUrl { get; set; } = "https://api.brevo.com/v3";

    // Brevo API key (the "xkeysib-..." key from SMTP & API → API Keys).
    public string ApiKey { get; set; } = string.Empty;
}
