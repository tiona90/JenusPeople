using API.Middleware;
using API.Extensions;
using API.Models;
using API.Hubs;
using API.BackgroundServices;
using API.Services;
using Application.Core;
using Application.Reminders;
using Application.AnnualLeaves.Queries;
using Application.Holidays;
using Application.LeaveTypes.Commands;
using Application.LeaveTypes.DTOs;
using Application.ProjectActivityTypes.Commands;
using Application.ProjectActivityTypes.DTOs;
using Asp.Versioning;
using Domain;
using Domain.Interfaces;
using FluentValidation;
using Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using API.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Persistence.Interceptors;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;


var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(kvp => kvp.Value is { Errors.Count: > 0 })
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                        ? "The input was not valid."
                        : e.ErrorMessage)
                    .ToArray());

        var response = new ApiErrorResponse
        {
            StatusCode = StatusCodes.Status400BadRequest,
            Message = "One or more validation errors occurred.",
            Path = context.HttpContext.Request.Path.Value ?? string.Empty,
            TraceId = context.HttpContext.TraceIdentifier,
            Timestamp = DateTime.UtcNow,
            Errors = errors
        };

        return new BadRequestObjectResult(response);
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("api-version"),
        new QueryStringApiVersionReader("api-version"));
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

// Names referenced by [EnableRateLimiting] attributes on controller actions.
const string AuthStrictPolicy = "auth-strict";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"message\":\"Too many requests. Please slow down and try again shortly.\"}",
            cancellationToken);
    };

    // Global fixed-window: 100 req/min per client IP.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });

    // Stricter sliding-window for credential endpoints: 5 attempts per minute per IP,
    // split into 6 segments (~10s) so the cap glides instead of resetting at the minute mark.
    options.AddPolicy(AuthStrictPolicy, context =>
    {
        var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });
});

builder.Services.AddSwaggerDocumentation();
builder.Services.AddSignalR();

// HSTS configuration — only emitted in non-Development (see app.UseHsts below).
// 180 days max-age + IncludeSubDomains is the conservative starting point;
// move to the 2-year `preload` value once you're ready to submit the domain
// to the HSTS preload list.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(180);
    options.IncludeSubDomains = true;
    options.Preload = false;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructureServices(builder.Configuration);
// Account-lifecycle emails (welcome invite, password reset, email change) and
// the client links inside them. Shared by AccountController and
// AdminUsersController so the link shape can't drift between them.
builder.Services.AddScoped<IAccountEmailSender, AccountEmailSender>();
builder.Services.AddScoped<AuditingSaveChangesInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    opt.AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
});
var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientPolicy", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                // localhost is always allowed for local development; additional
                // production origins come from the "Cors:AllowedOrigins" config.
                return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    || corsAllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            // Pagination metadata lives in headers (the body stays a plain array);
            // expose them so the browser client can read page/total.
            .WithExposedHeaders("X-Total-Count", "X-Page", "X-Page-Size");
    });
});
builder.Services.AddMediatR(x =>
x.RegisterServicesFromAssemblyContaining<GetAnnualLeaveList.Handler>());

// Reminder scheduling: the dispatcher builds/sends each reminder's content; the
// hosted service ticks every minute and fires due reminders (server local time,
// in-memory dedup). See ReminderBackgroundService for the scheduling rules.
builder.Services.AddScoped<ReminderDispatcher>();
builder.Services.AddHostedService<ReminderBackgroundService>();

// Nager (public-holidays API) is a public, occasionally-flaky third party. The
// standard resilience handler bundles: per-attempt timeout, retry with
// exponential backoff + jitter, circuit breaker (opens at 10% failure rate over
// a 30s window), total request timeout, and a concurrency limiter.
builder.Services.AddHttpClient<NagerHolidayClient>(client =>
{
    client.BaseAddress = new Uri("https://date.nager.at/api/v3/");
})
.AddStandardResilienceHandler(options =>
{
    // Per-attempt cap matches the previous HttpClient.Timeout (8s).
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
    // Cumulative cap across all retries — must exceed AttemptTimeout × (Retry.MaxRetryAttempts + 1).
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(40);
    // Circuit-breaker sampling window must be at least 2× AttemptTimeout.
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
});

// Explicit registrations for LeaveType commands to avoid handler resolution issues
// when running under watch/hot-reload in development.
builder.Services.AddTransient<IRequestHandler<CreateLeaveType.Command, Result<LeaveTypeDto>>, CreateLeaveType.Handler>();
builder.Services.AddTransient<IRequestHandler<UpdateLeaveType.Command, Result<LeaveTypeDto>>, UpdateLeaveType.Handler>();
builder.Services.AddTransient<IRequestHandler<DeleteLeaveType.Command, Result<Unit>>, DeleteLeaveType.Handler>();

builder.Services.AddTransient<IRequestHandler<CreateProjectActivityType.Command, Result<ProjectActivityTypeDto>>, CreateProjectActivityType.Handler>();
builder.Services.AddTransient<IRequestHandler<UpdateProjectActivityType.Command, Result<ProjectActivityTypeDto>>, UpdateProjectActivityType.Handler>();
builder.Services.AddTransient<IRequestHandler<DeleteProjectActivityType.Command, Result<Unit>>, DeleteProjectActivityType.Handler>();

builder.Services.AddAutoMapper(cfg => { }, typeof(MappingProfiles).Assembly);
builder.Services.AddValidatorsFromAssemblyContaining<MappingProfiles>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddIdentityApiEndpoints<User>(opt =>
{
    opt.User.RequireUniqueEmail = true;
    opt.SignIn.RequireConfirmedEmail = true;
    opt.SignIn.RequireConfirmedAccount = true;

    // Brute-force protection. Stated explicitly rather than inherited from the
    // framework defaults so the policy is reviewable here and pinned by tests:
    // the defaults are 5 attempts / 5 minutes, and silently depending on them
    // means a framework upgrade can change how hard this application is to
    // guess your way into. AccountController.Login opts in with
    // lockoutOnFailure: true, without which none of this counts.
    opt.Lockout.MaxFailedAccessAttempts = LockoutPolicy.MaxFailedAccessAttempts;
    opt.Lockout.DefaultLockoutTimeSpan = LockoutPolicy.LockoutDuration;
    opt.Lockout.AllowedForNewUsers = true;
})
.AddRoles<Role>()
    .AddEntityFrameworkStores<AppDbContext>();

// No external (Google/GitHub) providers are registered. Social sign-in was
// removed along with public self-registration: its callback provisioned an
// account for any unrecognised email, which is exactly the self-signup path
// this application must not expose. Accounts come from POST /api/AdminUsers.
builder.Services.AddAuthentication();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AnnualLeaveRead", policy =>
        policy.RequireRole(AppRoles.Admin, AppRoles.Manager, AppRoles.Employee));

    options.AddPolicy("AnnualLeaveCreate", policy =>
        policy.RequireRole(AppRoles.Admin, AppRoles.Manager, AppRoles.Employee));

    options.AddPolicy("AnnualLeaveUpdate", policy =>
        policy.RequireRole(AppRoles.Admin, AppRoles.Manager, AppRoles.Employee));

    options.AddPolicy("AnnualLeaveDelete", policy =>
        policy.RequireRole(AppRoles.Admin, AppRoles.Manager, AppRoles.Employee));

    options.AddPolicy("EmployeeProfileUpdate", policy =>
        policy.RequireRole(AppRoles.Admin));
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerDocumentation();
}
else
{
    // HSTS only outside Development — browsers cache the header per-host and
    // accidentally pinning localhost or a staging hostname is painful to
    // undo. Production traffic should already be on HTTPS by this point.
    app.UseHsts();
}

// SecurityHeaders runs first so its OnStarting callback is registered before
// any downstream middleware can call Response.Clear(); the callback fires on
// the actual flush, so error responses get the headers too.
app.UseMiddleware<SecurityHeadersMiddleware>();

// Configure the HTTP request pipeline.
app.UseMiddleware<GlobalExceptionMiddleware>();

// Serve the React SPA (published into wwwroot) same-origin: static assets
// first, then a fallback to index.html for client-side routes below.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("ClientPolicy");
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Lax,
    Secure = CookieSecurePolicy.SameAsRequest,
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// MapIdentityApi is deliberately NOT mapped. It publishes an anonymous
// POST /register (plus /resendConfirmationEmail, /refresh and /manage/*), which
// is exactly the public self-registration this application must not expose —
// mapping it would re-open that door behind AccountController's back.
// Everything the client needs is an explicit action on AccountController:
// login, logout, user-info, profile, profile-image, forgot-password,
// reset-password, verify-email and confirm-email-change. Accounts are created
// only by an administrator via POST /api/AdminUsers.
//
// AddIdentityApiEndpoints<User> above stays: it registers services only — the
// SignInManager and default token providers that AccountController's sign-in,
// password-reset and email-change flows depend on — not routes. With the
// token-issuing endpoint unmapped no bearer token can be obtained, so the
// identity cookie is the only way in.
app.MapHub<NotificationsHub>("/hubs/notifications");

// Any non-API, non-file request falls through to the SPA entry point so that
// client-side routes resolve on a full-page load / refresh. Unmatched /api and
// /hubs paths must 404 (so API callers get a proper status, not HTML).
app.MapFallback(async context =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

using var scope = app.Services.CreateScope();
var services = scope.ServiceProvider;
try
{
    var context = services.GetRequiredService<AppDbContext>();
    await context.Database.MigrateAsync();

    // Seeding is deliberately opt-in. Deployments only run migrations and keep the
    // users and business data that already exist in SQL Server.
    if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    {
        var userManager = services.GetRequiredService<UserManager<User>>();
        var roleManager = services.GetRequiredService<RoleManager<Role>>();

        // SeedPolicy, not the raw config values: it is what withholds account
        // creation and password resets on a Production host.
        var seedPolicy = SeedPolicy.For(
            app.Environment.EnvironmentName,
            demoData: app.Configuration.GetValue<bool>("Seed:DemoData"),
            allowInProduction: app.Configuration.GetValue<bool>("Seed:AllowInProduction"));

        if (seedPolicy.RestrictedForProduction)
        {
            services.GetRequiredService<ILogger<Program>>().LogWarning(
                "Seed:Enabled is set on a Production host. Demo accounts will not be seeded, and "
                + "no account will be created or have its password reset — doing so would apply the "
                + "seeder's built-in default password. Set Seed:AllowInProduction=true only to "
                + "deliberately bootstrap this database, and change the seeded password immediately.");
        }

        await DbInitializer.SeedData(context, userManager, roleManager, seedPolicy);
    }
}
catch (Exception ex)
{
    var logger = services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "An error accoured duaring migration");
}
app.Run();
