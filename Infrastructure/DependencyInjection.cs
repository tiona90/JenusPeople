using Domain.Interfaces;
using Infrastructure.Configuration;
using Infrastructure.Services;
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
