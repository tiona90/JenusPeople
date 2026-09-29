using Application.Core;
using Application.Files.Commands;
using Application.TaskSettings;
using Application.WorkTasks.DTOs;
using Application.WorkTasks.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.WorkTasks.Commands;

/// <summary>
/// Attaches one uploaded file to a task. Only whoever may edit the task
/// (<see cref="WorkTaskAccess.CanManageAsync"/>: its creator, or anyone in scope once
/// the creator is deactivated) may add one; assignees and bystanders open them. The
/// file goes through <see cref="StoreFile"/> like every other upload, and is stored
/// only once the caller is known to be allowed and the task not already full.
/// </summary>
public class AddWorkTaskAttachment
{
    public const string NotCreatorMessage = "Only the person who created this task can attach files to it.";

    public static string TooManyMessageFor(int max) =>
        $"A task can carry at most {max} attachments. Remove one to add another.";

    public class Command : IRequest<Result<WorkTaskDto>>
    {
        public int Id { get; set; }
        public string CallerUserId { get; set; } = string.Empty;
        public required byte[] Content { get; set; }
        public required string FileName { get; set; }
        public string? DeclaredContentType { get; set; }

        /// <summary>An Employee: only a task they are on or created is visible.</summary>
        public bool AssignedOnly { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<WorkTaskDto>>
    {
        public async Task<Result<WorkTaskDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var task = await WorkTaskAccess.FindVisibleAsync(context, request.Id, request.CallerUserId, cancellationToken, request.AssignedOnly);
            if (task is null)
                return Result<WorkTaskDto>.Failure(WorkTaskAccess.NotFoundMessage);
            if (!await WorkTaskAccess.CanManageAsync(context, task, request.CallerUserId, cancellationToken, request.AssignedOnly))
                return Result<WorkTaskDto>.Forbidden(NotCreatorMessage);

            var settings = await WorkTaskSettingsStore.LoadAsync(context, cancellationToken);
            if (await context.WorkTaskAttachments.CountAsync(a => a.WorkTaskId == task.Id, cancellationToken) >= settings.MaxAttachmentsPerTask)
                return Result<WorkTaskDto>.Invalid(TooManyMessageFor(settings.MaxAttachmentsPerTask));

            var stored = await new StoreFile.Handler(context).Handle(new StoreFile.Command
            {
                Content = request.Content,
                FileName = request.FileName,
                DeclaredContentType = request.DeclaredContentType,
                Purpose = StoredFilePurpose.TaskAttachment,
                UploadedById = request.CallerUserId,
                AcceptedKindsOverride = AllowedKinds(settings),
                MaxSizeBytesOverride = settings.MaxAttachmentSizeMb * 1024 * 1024,
            }, cancellationToken);
            if (!stored.IsSuccess)
                return Result<WorkTaskDto>.Invalid(stored.Error!);

            context.WorkTaskAttachments.Add(new WorkTaskAttachment
            {
                WorkTaskId = task.Id,
                StoredFileId = stored.Value!,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken);

            return Result<WorkTaskDto>.Success(await WorkTaskProjection.LoadDtoAsync(
                context, task.Id, request.CallerUserId, cancellationToken, callerManages: !request.AssignedOnly));
        }

        private static List<FileSignatureValidator.FileKind> AllowedKinds(WorkTaskSettings s)
        {
            var kinds = new List<FileSignatureValidator.FileKind>();
            if (s.AllowImages) kinds.AddRange([FileSignatureValidator.FileKind.Jpeg, FileSignatureValidator.FileKind.Png]);
            if (s.AllowPdf) kinds.Add(FileSignatureValidator.FileKind.Pdf);
            if (s.AllowWord) kinds.AddRange([FileSignatureValidator.FileKind.Docx, FileSignatureValidator.FileKind.Doc]);
            if (s.AllowExcel) kinds.AddRange([FileSignatureValidator.FileKind.Xlsx, FileSignatureValidator.FileKind.Xls]);
            return kinds;
        }
    }
}
