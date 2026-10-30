using Application.Core;
using API.Middleware;
using Application.SystemErrors;
using Application.SystemErrors.Queries;
using Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A System Administrator configures the system, so what they are emailed about
/// is the system misbehaving: an unhandled 500 in a request, or a reminder whose
/// dispatch threw. Every System Administrator with an email is told, nobody else
/// is, and the same fault is reported once an hour rather than once per request.
/// </summary>
public class SystemErrorNotifierTests
{
    private const string SysEmail = "sys@example.com";
    private const string SecondSysEmail = "sys2@example.com";
    private const string HrEmail = "hr@example.com";

    private static readonly DateTime Now = new(2026, 9, 24, 8, 0, 0, DateTimeKind.Utc);

    private static AppDbContext SeedWorld(bool emailEnabled = true)
    {
        var db = TestDb.Create();
        db.AppSettings.Add(new AppSettings { EmailNotificationsEnabled = emailEnabled });

        var sysRole = new Role { Id = "r-sys", Name = AppRoles.SystemAdministrator, NormalizedName = AppRoles.SystemAdministrator.ToUpperInvariant() };
        var hrRole = new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() };
        var mgrRole = new Role { Id = "r-mgr", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() };
        db.Roles.AddRange(sysRole, hrRole, mgrRole);

        db.Users.Add(new User { Id = "sys-u", UserName = "sys", DisplayName = "Sam System", Email = SysEmail, IsActive = true });
        db.UserRoles.Add(new UserRole { UserId = "sys-u", RoleId = sysRole.Id });
        db.Users.Add(new User { Id = "sys2-u", UserName = "sys2", DisplayName = "Sal System", Email = SecondSysEmail, IsActive = true });
        db.UserRoles.Add(new UserRole { UserId = "sys2-u", RoleId = sysRole.Id });
        // A System Administrator who left: switched off, so not mailed.
        db.Users.Add(new User { Id = "old-u", UserName = "old", DisplayName = "Old System", Email = "old@example.com", IsActive = false });
        db.UserRoles.Add(new UserRole { UserId = "old-u", RoleId = sysRole.Id });
        db.Users.Add(new User { Id = "hr-u", UserName = "hr", DisplayName = "Hope HR", Email = HrEmail, IsActive = true });
        db.UserRoles.Add(new UserRole { UserId = "hr-u", RoleId = hrRole.Id });
        db.Users.Add(new User { Id = "mgr-u", UserName = "mgr", DisplayName = "Mia Manager", Email = "mgr@example.com", IsActive = true });
        db.UserRoles.Add(new UserRole { UserId = "mgr-u", RoleId = mgrRole.Id });

        db.SaveChanges();
        return db;
    }

    private static SystemErrorNotifier NotifierFor(AppDbContext db, Domain.Interfaces.IEmailService email, SystemErrorThrottle? throttle = null) =>
        new(db, email, throttle ?? new SystemErrorThrottle(), NullLogger<SystemErrorNotifier>.Instance);

    private static SystemErrorReport Report(Exception ex, string source = "GET /api/timesheets", string? correlationId = "abc123", DateTime? at = null) =>
        new(source, ex, correlationId, at ?? Now);

    [Fact]
    public async Task Every_active_System_Administrator_is_told_and_nobody_else()
    {
        using var db = SeedWorld();
        var email = new FakeEmailService();

        var sent = await NotifierFor(db, email).NotifyAsync(Report(new InvalidCastException("boom")), CancellationToken.None);

        Assert.Equal(2, sent);
        Assert.Equivalent(new[] { SysEmail, SecondSysEmail }, email.Sent.Select(m => m.Recipient).ToArray());
        Assert.DoesNotContain(email.Sent, m => m.Recipient == HrEmail);
        Assert.DoesNotContain(email.Sent, m => m.Recipient == "old@example.com");
    }

    [Fact]
    public async Task The_message_names_where_when_and_what_went_wrong()
    {
        using var db = SeedWorld();
        var email = new FakeEmailService();
        Exception thrown;
        try { throw new InvalidCastException("Specified cast is not valid: <secret>"); }
        catch (Exception ex) { thrown = ex; }

        await NotifierFor(db, email).NotifyAsync(Report(thrown), CancellationToken.None);

        var mail = email.Sent.First(m => m.Recipient == SysEmail);
        Assert.Contains("system error", mail.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GET /api/timesheets", mail.Subject);
        Assert.Contains("Hello Sam System", mail.HtmlBody);
        Assert.Contains("GET /api/timesheets", mail.HtmlBody);
        Assert.Contains("abc123", mail.HtmlBody);
        Assert.Contains("24 Sep 2026 08:00", mail.HtmlBody);
        Assert.Contains("InvalidCastException", mail.HtmlBody);
        // The message is HTML-encoded, never pasted raw.
        Assert.Contains("Specified cast is not valid: &lt;secret&gt;", mail.HtmlBody);
        Assert.DoesNotContain("<secret>", mail.HtmlBody);
        // The stack trace points at the line that threw.
        Assert.Contains(nameof(The_message_names_where_when_and_what_went_wrong), mail.HtmlBody);
        // The plain-text twin carries the same facts.
        Assert.Contains("abc123", mail.TextBody);
        Assert.Contains("InvalidCastException", mail.TextBody);
    }

    [Fact]
    public async Task The_same_fault_is_reported_once_an_hour()
    {
        using var db = SeedWorld();
        var email = new FakeEmailService();
        var throttle = new SystemErrorThrottle();

        var first = await NotifierFor(db, email, throttle).NotifyAsync(Report(new InvalidCastException("a")), CancellationToken.None);
        // Same place, same kind of exception, a different message: still the same fault.
        var repeat = await NotifierFor(db, email, throttle).NotifyAsync(Report(new InvalidCastException("b"), at: Now.AddMinutes(30)), CancellationToken.None);
        // A different kind of exception at the same place is a different fault.
        var other = await NotifierFor(db, email, throttle).NotifyAsync(Report(new NullReferenceException("c"), at: Now.AddMinutes(30)), CancellationToken.None);
        // And once the hour is up, the first is worth repeating.
        var later = await NotifierFor(db, email, throttle).NotifyAsync(Report(new InvalidCastException("d"), at: Now.AddMinutes(61)), CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(0, repeat);
        Assert.Equal(2, other);
        Assert.Equal(2, later);
    }

    [Fact]
    public async Task Nothing_is_sent_while_email_notifications_are_switched_off()
    {
        using var db = SeedWorld(emailEnabled: false);
        var email = new FakeEmailService();

        var sent = await NotifierFor(db, email).NotifyAsync(Report(new InvalidCastException("boom")), CancellationToken.None);

        Assert.Equal(0, sent);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_failing_provider_never_throws_back_into_the_caller()
    {
        using var db = SeedWorld();
        var email = new ThrowingEmailService();

        var sent = await NotifierFor(db, email).NotifyAsync(Report(new InvalidCastException("boom")), CancellationToken.None);

        Assert.Equal(0, sent);
    }

    // ── The fault is recorded, so the System Administrator's bell can list it ──

    [Fact]
    public async Task The_fault_is_recorded_for_the_bell_as_well_as_emailed()
    {
        using var db = SeedWorld();
        var email = new FakeEmailService();

        await NotifierFor(db, email).NotifyAsync(Report(new InvalidCastException("boom")), CancellationToken.None);

        var row = Assert.Single(db.SystemErrors);
        Assert.Equal("GET /api/timesheets", row.Source);
        Assert.Equal(typeof(InvalidCastException).FullName, row.ExceptionType);
        Assert.Equal("boom", row.Message);
        Assert.Equal("abc123", row.CorrelationId);
        Assert.Equal(Now, row.OccurredAtUtc);
        Assert.Equal(Now, row.LastOccurredAtUtc);
        Assert.Equal(1, row.Occurrences);
    }

    [Fact]
    public async Task A_repeat_inside_the_hour_bumps_the_count_instead_of_adding_a_row()
    {
        using var db = SeedWorld();
        var email = new FakeEmailService();
        var throttle = new SystemErrorThrottle();

        await NotifierFor(db, email, throttle).NotifyAsync(Report(new InvalidCastException("a")), CancellationToken.None);
        await NotifierFor(db, email, throttle).NotifyAsync(Report(new InvalidCastException("b"), at: Now.AddMinutes(30)), CancellationToken.None);
        await NotifierFor(db, email, throttle).NotifyAsync(Report(new NullReferenceException("c"), at: Now.AddMinutes(31)), CancellationToken.None);
        await NotifierFor(db, email, throttle).NotifyAsync(Report(new InvalidCastException("d"), at: Now.AddMinutes(61)), CancellationToken.None);

        var rows = db.SystemErrors.OrderBy(e => e.OccurredAtUtc).ToList();
        Assert.Equal(3, rows.Count);
        var first = rows[0];
        Assert.Equal(typeof(InvalidCastException).FullName, first.ExceptionType);
        Assert.Equal(2, first.Occurrences);
        Assert.Equal(Now, first.OccurredAtUtc);
        Assert.Equal(Now.AddMinutes(30), first.LastOccurredAtUtc);
        // The first message is kept: a repeat's message is usually the same bug with a different row id.
        Assert.Equal("a", first.Message);
        Assert.Equal(typeof(NullReferenceException).FullName, rows[1].ExceptionType);
        Assert.Equal(1, rows[2].Occurrences);
        Assert.Equal("d", rows[2].Message);
    }

    [Fact]
    public async Task The_fault_is_recorded_even_while_email_notifications_are_switched_off()
    {
        using var db = SeedWorld(emailEnabled: false);
        var email = new FakeEmailService();

        await NotifierFor(db, email).NotifyAsync(Report(new InvalidCastException("boom")), CancellationToken.None);

        Assert.Empty(email.Sent);
        Assert.Single(db.SystemErrors);
    }

    [Fact]
    public async Task The_fault_is_recorded_even_when_the_provider_throws()
    {
        using var db = SeedWorld();

        await NotifierFor(db, new ThrowingEmailService()).NotifyAsync(Report(new InvalidCastException("boom")), CancellationToken.None);

        Assert.Single(db.SystemErrors);
    }

    [Fact]
    public async Task A_long_message_is_cut_to_fit_the_column()
    {
        using var db = SeedWorld();

        await NotifierFor(db, new FakeEmailService()).NotifyAsync(Report(new InvalidCastException(new string('x', 5000))), CancellationToken.None);

        Assert.Equal(SystemError.MessageMaxLength, Assert.Single(db.SystemErrors).Message.Length);
    }

    [Fact]
    public async Task The_list_is_newest_first_by_last_occurrence_and_bounded()
    {
        using var db = SeedWorld();
        db.SystemErrors.AddRange(
            new SystemError { Source = "a", ExceptionType = "T", Message = "m", OccurredAtUtc = Now.AddHours(-5), LastOccurredAtUtc = Now.AddHours(-5) },
            // Old, but still happening: it sorts by when it was last seen.
            new SystemError { Source = "b", ExceptionType = "T", Message = "m", OccurredAtUtc = Now.AddDays(-3), LastOccurredAtUtc = Now.AddHours(-1), Occurrences = 40 },
            new SystemError { Source = "c", ExceptionType = "T", Message = "m", OccurredAtUtc = Now.AddHours(-2), LastOccurredAtUtc = Now.AddHours(-2) });
        db.SaveChanges();

        var all = await new GetSystemErrorList.Handler(db).Handle(new GetSystemErrorList.Query(), CancellationToken.None);
        Assert.Equal(new[] { "b", "c", "a" }, all.Select(e => e.Source).ToArray());
        Assert.Equal(40, all[0].Occurrences);

        var two = await new GetSystemErrorList.Handler(db).Handle(new GetSystemErrorList.Query { Take = 2 }, CancellationToken.None);
        Assert.Equal(new[] { "b", "c" }, two.Select(e => e.Source).ToArray());
    }

    private sealed class ThrowingEmailService : Domain.Interfaces.IEmailService
    {
        public Task<bool> SendEmailAsync(string toEmail, string subject, string htmlBody, string? textBody = null, CancellationToken cancellationToken = default) =>
            throw new IOException("smtp down");
    }

    // ── The middleware is one of the two callers ─────────────────────────────

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "API";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<(HttpContext Context, FakeEmailService Email)> RunMiddlewareAsync(AppDbContext db, Exception toThrow, string path = "/api/timesheets")
    {
        var email = new FakeEmailService();
        var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<Domain.Interfaces.IEmailService>(email)
            .AddSingleton<SystemErrorThrottle>()
            .AddScoped<SystemErrorNotifier>()
            .AddLogging()
            .BuildServiceProvider();

        var middleware = new GlobalExceptionMiddleware(
            _ => throw toThrow,
            NullLogger<GlobalExceptionMiddleware>.Instance,
            new TestEnvironment(),
            new ConfigurationBuilder().Build());

        using var scope = services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, TraceIdentifier = "trace-42" };
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);
        return (context, email);
    }

    [Fact]
    public async Task An_unhandled_500_reaches_the_System_Administrators_with_the_correlation_id()
    {
        using var db = SeedWorld();

        var (context, email) = await RunMiddlewareAsync(db, new InvalidCastException("boom"));

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(2, email.Sent.Count);
        var mail = email.Sent.First(m => m.Recipient == SysEmail);
        Assert.Contains("GET /api/timesheets", mail.HtmlBody);
        Assert.Contains("trace-42", mail.HtmlBody);
    }

    [Fact]
    public async Task A_handled_4xx_is_not_a_system_error()
    {
        using var db = SeedWorld();

        // A refusal is mapped to 400: the caller's mistake, not the system's.
        var (context, email) = await RunMiddlewareAsync(db, new RequestRefusedException("no such row"));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Empty(email.Sent);
    }
}
