using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Issues;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Attachments;

/// <param name="Url">Signed download URL, valid for about an hour.</param>
/// <param name="ThumbnailUrl">Signed thumbnail URL for images that could be previewed.</param>
public sealed record AttachmentDto(
    int Id,
    int IssueId,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool IsImage,
    string UploadedByName,
    DateTimeOffset UploadedAt,
    string Url,
    string? ThumbnailUrl);

/// <summary>A file received from the client, read into memory (at most 10 MB).</summary>
public sealed record IncomingFile(string FileName, byte[] Content);

public sealed record UploadAttachmentsCommand(string IssueKey, IReadOnlyList<IncomingFile> Files);

internal static class AttachmentMapping
{
    public static async Task<List<AttachmentDto>> ListAsync(
        IApplicationDbContext db,
        IAttachmentUrlSigner signer,
        int userId,
        IQueryable<Attachment> attachments,
        CancellationToken cancellationToken)
    {
        var rows = await attachments.AsNoTracking()
            .OrderBy(a => a.UploadedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.IssueId, a.FileName, a.ContentType, a.SizeBytes, HasThumbnail = a.ThumbnailPath != null, a.UploadedBy.FullName, a.UploadedAt })
            .ToListAsync(cancellationToken);

        return rows.Select(a => new AttachmentDto(
                a.Id,
                a.IssueId,
                a.FileName,
                a.ContentType,
                a.SizeBytes,
                a.ContentType.StartsWith("image/", StringComparison.Ordinal),
                a.FullName,
                a.UploadedAt,
                signer.Sign(a.Id, thumbnail: false, userId),
                a.HasThumbnail ? signer.Sign(a.Id, thumbnail: true, userId) : null))
            .ToList();
    }
}

/// <summary>
/// Attach files to an issue while its sprint is in Testing (tester/admin). All files are checked first – one bad file
/// rejects the upload. Images get a thumbnail; each file writes an Attachment activity.
/// </summary>
public sealed class UploadAttachmentsHandler(
    IApplicationDbContext db,
    IFileStorage storage,
    IThumbnailGenerator thumbnails,
    IAttachmentUrlSigner signer,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<UploadAttachmentsHandler> logger)
{
    public const int MaxFilesPerUpload = 5;

    public async Task<Result<IReadOnlyList<AttachmentDto>>> HandleAsync(UploadAttachmentsCommand command, CancellationToken cancellationToken)
    {
        if (command.Files.Count is 0 or > MaxFilesPerUpload)
        {
            return Error.Validation("files", $"Attach between 1 and {MaxFilesPerUpload} files at a time.");
        }

        var inspected = new List<(IncomingFile File, FileKind Kind)>();
        foreach (var file in command.Files)
        {
            var (kind, error) = FileInspector.Inspect(file.FileName, file.Content);
            if (kind is null)
            {
                return Error.Validation("files", error!);
            }

            inspected.Add((file, kind));
        }

        var key = IssueRules.NormalizeKey(command.IssueKey);
        var issue = await db.Issues.AsNoTracking().Where(i => i.IssueKey == key).Select(i => new { i.Id, i.Sprint.Status }).SingleOrDefaultAsync(cancellationToken);
        if (issue is null)
        {
            return Error.NotFound("Issue");
        }

        var check = SprintRules.CanChangeIssues(currentUser.Role, issue.Status);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        var now = clock.UtcNow;
        var written = new List<string>();
        var added = new List<Attachment>();
        try
        {
            foreach (var (file, kind) in inspected)
            {
                var name = FileInspector.CleanFileName(file.FileName, kind);
                var basePath = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}";
                var storagePath = basePath + kind.Extension;
                await storage.SaveAsync(storagePath, new MemoryStream(file.Content), cancellationToken);
                written.Add(storagePath);

                string? thumbnailPath = null;
                if (kind.IsImage && thumbnails.Create(file.Content) is { } preview)
                {
                    thumbnailPath = basePath + "_thumb" + thumbnails.Extension;
                    await storage.SaveAsync(thumbnailPath, new MemoryStream(preview), cancellationToken);
                    written.Add(thumbnailPath);
                }

                var attachment = new Attachment
                {
                    IssueId = issue.Id,
                    FileName = name,
                    ContentType = kind.ContentType,
                    SizeBytes = file.Content.Length,
                    StoragePath = storagePath,
                    ThumbnailPath = thumbnailPath,
                    UploadedById = currentUser.UserId,
                    UploadedAt = now,
                    Activity = new IssueActivity
                    {
                        IssueId = issue.Id,
                        UserId = currentUser.UserId,
                        Kind = ActivityKind.Attachment,
                        Body = name,
                        CreatedAt = now,
                    },
                };
                db.Attachments.Add(attachment);
                added.Add(attachment);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            foreach (var path in written)
            {
                await AttachmentFiles.TryDeleteAsync(storage, path, logger);
            }

            throw;
        }

        var ids = added.Select(a => a.Id).ToList();
        return await AttachmentMapping.ListAsync(db, signer, currentUser.UserId, db.Attachments.Where(a => ids.Contains(a.Id)), cancellationToken);
    }
}

public sealed class ListAttachmentsHandler(IApplicationDbContext db, IAttachmentUrlSigner signer, ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<AttachmentDto>>> HandleAsync(string issueKey, CancellationToken cancellationToken)
    {
        var key = IssueRules.NormalizeKey(issueKey);
        var issueId = await db.Issues.AsNoTracking().Where(i => i.IssueKey == key).Select(i => (int?)i.Id).SingleOrDefaultAsync(cancellationToken);
        if (issueId is null)
        {
            return Error.NotFound("Issue");
        }

        return await AttachmentMapping.ListAsync(db, signer, currentUser.UserId, db.Attachments.Where(a => a.IssueId == issueId), cancellationToken);
    }
}

/// <summary>Remove an attachment while the sprint is in Testing (tester/admin); the timeline keeps a note of it.</summary>
public sealed class DeleteAttachmentHandler(
    IApplicationDbContext db,
    IFileStorage storage,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<DeleteAttachmentHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(int id, CancellationToken cancellationToken)
    {
        var attachment = await db.Attachments.Include(a => a.Issue).ThenInclude(i => i.Sprint).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (attachment is null)
        {
            return Error.NotFound("Attachment");
        }

        var check = SprintRules.CanChangeIssues(currentUser.Role, attachment.Issue.Sprint.Status);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        db.Attachments.Remove(attachment);
        db.IssueActivities.Add(new IssueActivity
        {
            IssueId = attachment.IssueId,
            UserId = currentUser.UserId,
            Kind = ActivityKind.Attachment,
            Body = $"Removed {attachment.FileName}",
            OldValue = attachment.FileName.Length > 500 ? attachment.FileName[..500] : attachment.FileName,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        await AttachmentFiles.TryDeleteAsync(storage, attachment.StoragePath, logger);
        if (attachment.ThumbnailPath is not null)
        {
            await AttachmentFiles.TryDeleteAsync(storage, attachment.ThumbnailPath, logger);
        }

        return true;
    }
}

public sealed record AttachmentFile(Stream Content, string ContentType, string FileName, bool Inline);

/// <param name="UserId">The user from a valid signed URL, or the bearer token's user.</param>
public sealed record OpenAttachmentQuery(int Id, bool Thumbnail, int UserId);

/// <summary>The file behind a download / thumbnail URL. The requesting user must still be active.</summary>
public sealed class OpenAttachmentHandler(IApplicationDbContext db, IFileStorage storage, IThumbnailGenerator thumbnails)
{
    public async Task<Result<AttachmentFile>> HandleAsync(OpenAttachmentQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == query.UserId && u.IsActive, cancellationToken))
        {
            return Error.Forbidden("attachment_forbidden", "You are not allowed to open this attachment.");
        }

        var attachment = await db.Attachments.AsNoTracking()
            .Where(a => a.Id == query.Id)
            .Select(a => new { a.FileName, a.ContentType, a.StoragePath, a.ThumbnailPath })
            .SingleOrDefaultAsync(cancellationToken);
        var path = query.Thumbnail ? attachment?.ThumbnailPath : attachment?.StoragePath;
        if (attachment is null || path is null || storage.OpenRead(path) is not { } content)
        {
            return Error.NotFound("Attachment");
        }

        return query.Thumbnail
            ? new AttachmentFile(content, thumbnails.ContentType, Path.ChangeExtension(attachment.FileName, thumbnails.Extension), Inline: true)
            : new AttachmentFile(content, attachment.ContentType, attachment.FileName, Inline: IsSafeInline(attachment.ContentType));
    }

    /// <summary>Images, PDF and plain text open in the browser; Office files download.</summary>
    private static bool IsSafeInline(string contentType) =>
        contentType.StartsWith("image/", StringComparison.Ordinal) || contentType is "application/pdf" or "text/plain";
}

internal static class AttachmentFiles
{
    /// <summary>Best effort: a file left behind is only wasted disk, the database stays correct.</summary>
    public static async Task TryDeleteAsync(IFileStorage storage, string path, ILogger logger)
    {
        try
        {
            await storage.DeleteAsync(path, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete attachment file {Path}", path);
        }
    }
}
