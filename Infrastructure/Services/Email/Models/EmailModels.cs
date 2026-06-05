namespace Infrastructure.Services.Email.Models;

/// <summary>
/// Email provider type. Selected via the "Email:Provider" configuration value.
/// </summary>
public enum EmailProviderType
{
    /// <summary>SMTP-based email (Gmail, Office 365, Brevo SMTP relay, etc.).</summary>
    Smtp,

    /// <summary>Brevo (formerly Sendinblue) transactional HTTP API.</summary>
    Brevo
}

/// <summary>
/// Email configuration. Chooses which registered <c>IEmailProvider</c> handles sends.
/// </summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The provider to use (Smtp or Brevo).</summary>
    public EmailProviderType Provider { get; set; } = EmailProviderType.Smtp;
}

/// <summary>A single email contact (name + address).</summary>
public class EmailContact
{
    public string? Name { get; set; }
    public string Email { get; set; } = string.Empty;
}

/// <summary>A provider-agnostic email message.</summary>
public class EmailMessage
{
    public EmailContact From { get; set; } = new();
    public List<EmailContact> To { get; set; } = [];
    public List<EmailContact>? Cc { get; set; }
    public List<EmailContact>? Bcc { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? TextContent { get; set; }
    public string? HtmlContent { get; set; }
    public List<EmailAttachment>? Attachments { get; set; }
}

/// <summary>An email attachment.</summary>
public class EmailAttachment
{
    public string Name { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = "application/octet-stream";
}

/// <summary>Result of a single send attempt.</summary>
public class EmailSendResult
{
    public bool IsSuccess { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public int StatusCode { get; set; }

    public static EmailSendResult Success(string? messageId = null) => new()
    {
        IsSuccess = true,
        MessageId = messageId,
        StatusCode = 200
    };

    public static EmailSendResult Failure(string errorMessage, int statusCode = 500) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage,
        StatusCode = statusCode
    };
}
