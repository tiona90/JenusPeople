using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Infrastructure.Configuration;
using Infrastructure.Services.Email.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.Email.Providers;

/// <summary>
/// Sends mail through the Brevo (formerly Sendinblue) transactional HTTP API
/// (POST https://api.brevo.com/v3/smtp/email with an "api-key" header).
/// Requires a verified sender/domain in Brevo. Note: if the Brevo account has
/// "Authorised IPs" enabled, the calling server's IP must be allowlisted or
/// Brevo returns 401 — switch to the SMTP provider to avoid that restriction.
/// </summary>
public class BrevoEmailProvider : IEmailProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BrevoEmailProvider> _logger;
    private readonly BrevoOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;

    public BrevoEmailProvider(
        HttpClient httpClient,
        IOptions<BrevoOptions> options,
        ILogger<BrevoEmailProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        // Base URL ends with a trailing slash so the relative "smtp/email" resolves.
        var baseUrl = _options.BaseUrl.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);

        _httpClient.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("api-key", _options.ApiKey);
        }
        _httpClient.DefaultRequestHeaders.Add("accept", "application/json");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public EmailProviderType ProviderType => EmailProviderType.Brevo;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Brevo email skipped: no API key configured.");
            return EmailSendResult.Failure("Brevo API key is not configured.", 500);
        }

        try
        {
            _logger.LogInformation("Sending email via Brevo to: {Recipients}",
                string.Join(", ", message.To.Select(t => t.Email)));

            var brevoRequest = MapToBrevoRequest(message);
            var json = JsonSerializer.Serialize(brevoRequest, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("smtp/email", content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<TransactionalEmailResponse>(responseContent, _jsonOptions);
                _logger.LogInformation("Brevo email sent successfully. MessageId: {MessageId}", result?.MessageId);
                return EmailSendResult.Success(result?.MessageId);
            }

            var error = JsonSerializer.Deserialize<BrevoErrorResponse>(responseContent, _jsonOptions);
            _logger.LogWarning("Brevo rejected email: {Error} (Code: {Code}, Status: {Status})",
                error?.Message, error?.Code, (int)response.StatusCode);
            return EmailSendResult.Failure(
                error?.Message ?? "Unknown error from Brevo",
                (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while sending email via Brevo.");
            return EmailSendResult.Failure($"HTTP error: {ex.Message}", 503);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            _logger.LogWarning("Brevo email send was cancelled.");
            return EmailSendResult.Failure("Request was cancelled.", 499);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending email via Brevo.");
            return EmailSendResult.Failure(ex.Message, 500);
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("account", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test Brevo connection.");
            return false;
        }
    }

    private static TransactionalEmailRequest MapToBrevoRequest(EmailMessage message)
    {
        var request = new TransactionalEmailRequest
        {
            Sender = new BrevoEmailContact { Name = message.From.Name, Email = message.From.Email },
            To = message.To.Select(c => new BrevoEmailContact { Name = c.Name, Email = c.Email }).ToList(),
            Subject = message.Subject,
            TextContent = message.TextContent,
            HtmlContent = message.HtmlContent ?? string.Empty
        };

        if (message.Cc?.Count > 0)
        {
            request.Cc = message.Cc.Select(c => new BrevoEmailContact { Name = c.Name, Email = c.Email }).ToList();
        }

        if (message.Bcc?.Count > 0)
        {
            request.Bcc = message.Bcc.Select(c => new BrevoEmailContact { Name = c.Name, Email = c.Email }).ToList();
        }

        return request;
    }
}
