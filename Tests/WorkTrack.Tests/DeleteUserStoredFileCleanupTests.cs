using Application.AdminUsers.Commands;
using Application.Core;
using Application.Files;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Deleting a user who had uploaded anything — a profile photo, a doctor's note, a
/// handover document — failed on the server with a 500 while working on a developer
/// machine. StoredFile.UploadedById is DeleteBehavior.Restrict and the sweep in
/// DeleteAdminUser never touched it, so SQL Server refused the DELETE
/// ("FK_StoredFiles_AspNetUsers_UploadedById"). The demo accounts on a developer box
/// have no uploads, which is why it only showed in production.
///
/// SQLite rather than the in-memory provider: this is a foreign-key failure, and the
/// in-memory provider enforces none.
/// </summary>
public class DeleteUserStoredFileCleanupTests : IAsyncLifetime
{
    private const string AdminUserId = "u-admin";
    private const string OtherAdminUserId = "u-admin-2";
    private const string EmployeeUserId = "u-employee";

    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        await _connection.OpenAsync();

        var collection = new ServiceCollection();
        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        collection.AddIdentityCore<User>(o => o.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>();

        _services = collection.BuildServiceProvider();
        await Db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();
    private UserManager<User> Users => _services.GetRequiredService<UserManager<User>>();

    private Task<Result<Unit>> DeleteAsync(string userId, string requestedBy = AdminUserId) =>
        new DeleteAdminUser.Handler(Db, Users).Handle(
            new DeleteAdminUser.Command { Id = userId, RequestingUserId = requestedBy },
            CancellationToken.None);

    private async Task<User> AddUserAsync(string id, string name)
    {
        var user = new User
        {
            Id = id,
            UserName = $"{name}@test.local",
            Email = $"{name}@test.local",
            DisplayName = name,
            EmailConfirmed = true,
        };
        var created = await Users.CreateAsync(user);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        return user;
    }

    private static StoredFile FileUploadedBy(string userId, StoredFilePurpose purpose) => new()
    {
        Content = [1, 2, 3],
        FileName = "upload.bin",
        ContentType = "application/octet-stream",
        Sha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        SizeBytes = 3,
        Purpose = purpose,
        UploadedById = userId,
    };

    private static AnnualLeave LeaveFor(string userId, string id, string? evidenceUrl) => new()
    {
        Id = id,
        EmployeeId = userId,
        Status = AnnualLeaveStatus.Approved,
        Reason = "Sick",
        StartDate = new DateTime(2026, 3, 2),
        EndDate = new DateTime(2026, 3, 3),
        EvidenceUrl = evidenceUrl,
    };

    [Fact]
    public async Task Deleting_a_user_who_uploaded_a_profile_photo_succeeds_and_drops_the_photo()
    {
        await AddUserAsync(AdminUserId, "admin");
        var employee = await AddUserAsync(EmployeeUserId, "employee");

        var photo = FileUploadedBy(EmployeeUserId, StoredFilePurpose.ProfileImage);
        Db.StoredFiles.Add(photo);
        employee.ImageUrl = StoredFilePath.For(photo.Id);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var result = await DeleteAsync(EmployeeUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(await Users.FindByIdAsync(EmployeeUserId));
        Assert.False(await Db.StoredFiles.AnyAsync(f => f.Id == photo.Id));
    }

    [Fact]
    public async Task Evidence_on_the_users_own_leave_goes_with_them()
    {
        await AddUserAsync(AdminUserId, "admin");
        await AddUserAsync(EmployeeUserId, "employee");

        var note = FileUploadedBy(EmployeeUserId, StoredFilePurpose.LeaveEvidence);
        Db.StoredFiles.Add(note);
        Db.AnnualLeaves.Add(LeaveFor(EmployeeUserId, "L-own", StoredFilePath.For(note.Id)));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var result = await DeleteAsync(EmployeeUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await Db.StoredFiles.AnyAsync(f => f.Id == note.Id));
        Assert.False(await Db.AnnualLeaves.AnyAsync(l => l.Id == "L-own"));
    }

    /// <summary>
    /// The case the Restrict on the FK exists for: an admin uploaded evidence onto
    /// somebody else's leave, then leaves the company. The leave and its document
    /// are the employee's record, not the admin's, so the file is handed to the
    /// admin doing the deleting rather than deleted with the uploader.
    /// </summary>
    [Fact]
    public async Task Evidence_an_admin_uploaded_onto_someone_elses_leave_survives_and_is_handed_on()
    {
        await AddUserAsync(AdminUserId, "admin");
        await AddUserAsync(OtherAdminUserId, "admin-two");
        await AddUserAsync(EmployeeUserId, "employee");

        var note = FileUploadedBy(OtherAdminUserId, StoredFilePurpose.LeaveEvidence);
        var ownPhoto = FileUploadedBy(OtherAdminUserId, StoredFilePurpose.ProfileImage);
        Db.StoredFiles.AddRange(note, ownPhoto);
        Db.AnnualLeaves.Add(LeaveFor(EmployeeUserId, "L-employee", StoredFilePath.For(note.Id)));
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var result = await DeleteAsync(OtherAdminUserId, requestedBy: AdminUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(await Users.FindByIdAsync(OtherAdminUserId));

        var kept = await Db.StoredFiles.AsNoTracking().SingleAsync(f => f.Id == note.Id);
        Assert.Equal(AdminUserId, kept.UploadedById);
        Assert.Equal(new byte[] { 1, 2, 3 }, kept.Content);

        var leave = await Db.AnnualLeaves.AsNoTracking().SingleAsync(l => l.Id == "L-employee");
        Assert.Equal(StoredFilePath.For(note.Id), leave.EvidenceUrl);

        Assert.False(await Db.StoredFiles.AnyAsync(f => f.Id == ownPhoto.Id));
    }

    /// <summary>
    /// The reason the sweep has to do anything at all. If this is ever relaxed to
    /// Cascade or SetNull, whoever does it should see this test and decide what the
    /// sweep should do about files a surviving leave still points at.
    /// </summary>
    [Fact]
    public void The_uploader_foreign_key_still_restricts_deletes()
    {
        using var db = TestDb.Create();

        var fk = db.Model
            .FindEntityType(typeof(StoredFile))!
            .GetForeignKeys()
            .Single(k => k.Properties.Any(p => p.Name == nameof(StoredFile.UploadedById)));

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    private async Task<WorkTask> TaskWithAttachmentAsync(string createdBy, string attachedBy)
    {
        if (!await Db.Departments.AnyAsync(d => d.Id == 1))
            Db.Departments.Add(new Department { Id = 1, Name = "Sales", Code = "SAL" });
        var file = FileUploadedBy(attachedBy, StoredFilePurpose.TaskAttachment);
        var task = new WorkTask
        {
            Title = "Brief", DepartmentId = 1, CreatedById = createdBy,
            Assignees = [new WorkTaskAssignee { UserId = createdBy }],
            Attachments = [new WorkTaskAttachment { StoredFile = file }],
        };
        Db.WorkTasks.Add(task);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return task;
    }

    [Fact]
    public async Task A_file_the_user_attached_to_a_colleagues_task_stays_with_the_task()
    {
        await AddUserAsync(AdminUserId, "admin");
        await AddUserAsync(EmployeeUserId, "employee");
        await AddUserAsync(OtherAdminUserId, "colleague");
        var task = await TaskWithAttachmentAsync(createdBy: OtherAdminUserId, attachedBy: EmployeeUserId);

        var result = await DeleteAsync(EmployeeUserId);

        Assert.True(result.IsSuccess, result.Error);
        var attachment = await Db.WorkTaskAttachments.Include(a => a.StoredFile).SingleAsync(a => a.WorkTaskId == task.Id);
        Assert.Equal(AdminUserId, attachment.StoredFile!.UploadedById);
    }

    [Fact]
    public async Task Files_colleagues_attached_to_the_users_own_task_go_with_the_task()
    {
        await AddUserAsync(AdminUserId, "admin");
        await AddUserAsync(EmployeeUserId, "employee");
        await AddUserAsync(OtherAdminUserId, "colleague");
        var task = await TaskWithAttachmentAsync(createdBy: EmployeeUserId, attachedBy: OtherAdminUserId);

        var result = await DeleteAsync(EmployeeUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await Db.WorkTasks.AnyAsync(t => t.Id == task.Id));
        Assert.False(await Db.StoredFiles.AnyAsync(f => f.Purpose == StoredFilePurpose.TaskAttachment));
    }
}
