using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using AutoMapper;
using Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// CreateAnnualLeave interpolated the requester's free-text Reason and two display
/// names straight into the HTML body of the manager's email, while
/// UpdateLeaveStatus HtmlEncoded all six of its values. Same email, two answers:
/// anything an employee typed was markup the manager's mail client would render in
/// one of them and characters on the page in the other.
///
/// Both build their bodies through <see cref="NotificationEmail"/> now, which
/// encodes on the way in. These cover the builder's own rules and then the two
/// handlers end to end, because the point is not that a helper encodes — it is that
/// these two emails go through it.
/// </summary>
public class NotificationEmailTests
{
    private const string Markup = "<script>alert(1)</script>";
    private const string EncodedMarkup = "&lt;script&gt;alert(1)&lt;/script&gt;";

    /* ── The builder ────────────────────────────────────────────────────────── */

    [Fact]
    public void An_interpolated_value_is_encoded_and_emphasised()
    {
        var body = NotificationEmail.To("Manager")
            .Sentence($"A request from {Markup}.")
            .Build();

        Assert.Contains($"<strong>{EncodedMarkup}</strong>", body.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", body.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Plain drops the emphasis, not the encoding — it is for values that read as
    /// prose rather than as one of the message's headline facts.
    /// </summary>
    [Fact]
    public void A_plain_value_is_still_encoded()
    {
        var body = NotificationEmail.To("Employee")
            .Sentence($"Approved by {NotificationEmail.Plain(Markup)}.")
            .Build();

        Assert.Contains($"by {EncodedMarkup}.", body.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<strong>", body.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", body.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_recipient_name_and_details_are_encoded()
    {
        var body = NotificationEmail.To(Markup)
            .Detail("Reason", Markup)
            .Build();

        Assert.Contains($"<p>Hello {EncodedMarkup},</p>", body.Html, StringComparison.Ordinal);
        Assert.Contains($"<strong>Reason:</strong> {EncodedMarkup}", body.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", body.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The text alternative is not markup, so it carries what the user actually
    /// typed — encoding it would show readers entity references where they typed
    /// punctuation.
    /// </summary>
    [Fact]
    public void The_text_body_keeps_the_values_as_typed()
    {
        var body = NotificationEmail.To("Manager")
            .Sentence($"A request from {Markup}.")
            .Detail("Reason", Markup)
            .Closing("Please log in.")
            .Build();

        Assert.Contains($"A request from {Markup}.", body.Text, StringComparison.Ordinal);
        Assert.Contains($"Reason: {Markup}", body.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("&lt;", body.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_detail_is_left_out_entirely()
    {
        var body = NotificationEmail.To("Manager")
            .Detail("Reason", "   ")
            .Build();

        Assert.DoesNotContain("Reason", body.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("Reason", body.Text, StringComparison.Ordinal);
    }

    /* ── The two handlers ───────────────────────────────────────────────────── */

    private const int DepartmentId = 4;
    private const int LeaveTypeId = 7;
    private const string EmployeeUserId = "u-employee";
    private const string EmployeeProfileId = "p-employee";
    private const string ManagerUserId = "u-manager";
    private const string ManagerProfileId = "p-manager";
    private const string LeaveId = "L-1";

    private static readonly DateTime LeaveStart = new(2024, 3, 4);
    private static readonly DateTime LeaveEnd = new(2024, 3, 8);

    private static IMapper BuildMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfiles>(),
            NullLoggerFactory.Instance).CreateMapper();

    /// <summary>
    /// Both display names carry markup, so an unencoded body shows it whichever
    /// name the email happens to greet.
    /// </summary>
    private static async Task<AppDbContext> SeedWorldAsync(bool requiresApproval)
    {
        var db = TestDb.Create();

        db.Departments.Add(new Department { Id = DepartmentId, Name = "Engineering", Code = "ENG" });
        db.Users.Add(new User
        {
            Id = EmployeeUserId,
            UserName = "employee@test.local",
            Email = "employee@test.local",
            DisplayName = Markup,
        });
        db.Users.Add(new User
        {
            Id = ManagerUserId,
            UserName = "manager@test.local",
            Email = "manager@test.local",
            DisplayName = Markup,
        });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ManagerProfileId,
            UserId = ManagerUserId,
            DepartmentId = DepartmentId,
        });
        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = EmployeeProfileId,
            UserId = EmployeeUserId,
            DepartmentId = DepartmentId,
            // Makes the manager above the recipient of the new-request email.
            ManagerId = ManagerProfileId,
            AnnualLeaveEntitlement = 25,
            LeaveBalance = 25,
        });
        db.LeaveTypes.Add(new LeaveType
        {
            Id = LeaveTypeId,
            Name = "Annual",
            IsActive = true,
            AffectsBalance = true,
            RequiresApproval = requiresApproval,
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task The_new_request_email_encodes_the_reason_and_the_names()
    {
        await using var db = await SeedWorldAsync(requiresApproval: true);
        var mail = new FakeEmailService();

        var result = await new CreateAnnualLeave.Handler(db, BuildMapper(), mail).Handle(
            new CreateAnnualLeave.Command
            {
                AnnualLeave = new CreateAnnualLeaveRequest
                {
                    EmployeeId = EmployeeUserId,
                    LeaveTypeId = LeaveTypeId,
                    Reason = Markup,
                    StartDate = LeaveStart,
                    EndDate = LeaveEnd,
                },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, mail.SentCount);
        Assert.NotNull(mail.LastHtmlBody);

        // Three values reach this body from a person: the manager's name, the
        // employee's name and the reason. None of them may arrive as markup.
        Assert.DoesNotContain("<script>", mail.LastHtmlBody, StringComparison.Ordinal);
        Assert.Contains(EncodedMarkup, mail.LastHtmlBody, StringComparison.Ordinal);
        Assert.Contains($"<strong>Reason:</strong> {EncodedMarkup}", mail.LastHtmlBody, StringComparison.Ordinal);

        // The plain-text alternative still reads as the employee typed it.
        Assert.Contains($"Reason: {Markup}", mail.LastTextBody!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_status_change_email_encodes_the_comment_and_the_names()
    {
        await using var db = await SeedWorldAsync(requiresApproval: true);
        db.AnnualLeaves.Add(new AnnualLeave
        {
            Id = LeaveId,
            EmployeeId = EmployeeUserId,
            EmployeeProfileId = EmployeeProfileId,
            DepartmentId = DepartmentId,
            LeaveTypeId = LeaveTypeId,
            Status = AnnualLeaveStatus.Pending,
            Reason = "Family holiday",
            StartDate = LeaveStart,
            EndDate = LeaveEnd,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var mail = new FakeEmailService();

        var result = await new UpdateLeaveStatus.Handler(db, mail, new FakeChatNotificationService()).Handle(
            new UpdateLeaveStatus.Command
            {
                LeaveId = LeaveId,
                ChangedByUserId = ManagerUserId,
                IsAdmin = true,
                Request = new UpdateLeaveStatusRequest
                {
                    Status = AnnualLeaveStatus.Approved,
                    StatusComment = Markup,
                },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, mail.SentCount);
        Assert.NotNull(mail.LastHtmlBody);

        Assert.DoesNotContain("<script>", mail.LastHtmlBody, StringComparison.Ordinal);
        Assert.Contains($"<strong>Comment:</strong> {EncodedMarkup}", mail.LastHtmlBody, StringComparison.Ordinal);
        Assert.Contains($"Comment: {Markup}", mail.LastTextBody!, StringComparison.Ordinal);
    }
}
