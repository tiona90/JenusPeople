using Domain.Interfaces;
using Infrastructure.Configuration;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Resend;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions();
        services.Configure<AppUrlOptions>(configuration.GetSection(AppUrlOptions.SectionName));
        services.Configure<CloudinaryOptions>(configuration.GetSection(CloudinaryOptions.SectionName));
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IFileUploadService, CloudinaryFileUploadService>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddHttpClient<ResendClient>();
        services.Configure<ResendClientOptions>(options =>
        {
            options.ApiToken = configuration["Resend:ApiToken"] ?? string.Empty;
        });
        services.AddTransient<IResend, ResendClient>();

        return services;
    }
}
