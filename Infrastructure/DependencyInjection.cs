using System.Net;
using System.Net.Sockets;
using Domain.Interfaces;
using Infrastructure.Configuration;
using Infrastructure.Services;
using Infrastructure.Services.Email;
using Infrastructure.Services.Email.Models;
using Infrastructure.Services.Email.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions();
        services.Configure<AppUrlOptions>(configuration.GetSection(AppUrlOptions.SectionName));
        services.Configure<CloudinaryOptions>(configuration.GetSection(CloudinaryOptions.SectionName));
        services.Configure<SlackOptions>(configuration.GetSection(SlackOptions.SectionName));
        services.Configure<MailSettings>(configuration.GetSection(MailSettings.SectionName));
        services.Configure<BrevoOptions>(configuration.GetSection(BrevoOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        // Pluggable email providers: both are registered, and EmailService picks
        // the one matching the "Email:Provider" config value. SMTP (Gmail/Brevo
        // relay) avoids the Brevo HTTP API's "Authorised IPs" restriction; the
        // Brevo API provider remains available by flipping the config.
        // EmailService swallows failures (returns false) rather than throwing,
        // so a slow/unavailable provider never breaks a user request.
        services.AddScoped<IEmailProvider, SmtpEmailProvider>();
        services.AddHttpClient<IEmailProvider, BrevoEmailProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        })
        // Force the Brevo API connection over IPv4. Brevo's "Authorised IPs"
        // allowlist contains this host's stable public IPv4, but on a dual-stack
        // machine .NET prefers IPv6 — whose outbound privacy addresses rotate and
        // are NOT allowlisted, yielding intermittent 401 "unrecognised IP". Pinning
        // IPv4 keeps the source address stable and allowlisted.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(
                    context.DnsEndPoint.Host, AddressFamily.InterNetwork, cancellationToken);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                try
                {
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        });
        services.AddScoped<IEmailService, EmailService>();

        services.AddScoped<IFileUploadService, CloudinaryFileUploadService>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

        // Typed HttpClient for Slack incoming-webhook POSTs. Short timeout — we
        // never want a slow Slack to delay a user-facing response. The service
        // swallows exceptions, so no resilience handler is wired up; if Slack
        // is down the message is lost rather than retried.
        services.AddHttpClient<IChatNotificationService, SlackNotificationService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        return services;
    }
}
