namespace Infrastructure.Services.Email.Models;

/// <summary>Payload for Brevo's POST /v3/smtp/email transactional endpoint.</summary>
public class TransactionalEmailRequest
{
    /// <summary>Sender (must be a verified sender/domain in Brevo).</summary>
    public BrevoEmailContact Sender { get; set; } = new();

    public List<BrevoEmailContact> To { get; set; } = [];

    public List<BrevoEmailContact>? Cc { get; set; }

    public List<BrevoEmailContact>? Bcc { get; set; }

    public BrevoEmailContact? ReplyTo { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string HtmlContent { get; set; } = string.Empty;

    /// <summary>Plain-text fallback for non-HTML clients.</summary>
    public string? TextContent { get; set; }
}

/// <summary>Brevo contact shape (name + email).</summary>
public class BrevoEmailContact
{
    public string? Name { get; set; }
    public string Email { get; set; } = string.Empty;
}

/// <summary>Successful Brevo send response.</summary>
public class TransactionalEmailResponse
{
    public string MessageId { get; set; } = string.Empty;
}

/// <summary>Brevo error response body.</summary>
public class BrevoErrorResponse
{
    public string? Code { get; set; }
    public string? Message { get; set; }
}
