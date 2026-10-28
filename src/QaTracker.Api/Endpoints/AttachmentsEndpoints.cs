using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Features.Attachments;

namespace QaTracker.Api.Endpoints;

/// <summary>
/// Attachments (plan 14). Upload/remove: testers and admins while the sprint is in Testing. Download and thumbnail work
/// with a bearer token or with the signed URL from the attachment list (for &lt;img src&gt;); a wrong or expired
/// signature is 403, no credentials at all 401.
/// </summary>
public static class AttachmentsEndpoints
{
    /// <summary>5 files × 10 MB plus multipart overhead.</summary>
    private const long MaxRequestBytes = (UploadAttachmentsHandler.MaxFilesPerUpload * FileInspector.MaxBytes) + (1024 * 1024);

    public static IEndpointRouteBuilder MapAttachmentsEndpoints(this IEndpointRouteBuilder app)
    {
        var issues = app.MapGroup("/api/issues/{key}/attachments").WithTags("Attachments");

        issues.MapGet("/", async (string key, ListAttachmentsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(key, ct)).ToHttp(attachments => TypedResults.Ok(attachments)))
            .WithSummary("Attachments of the issue with signed download/thumbnail URLs (valid about an hour).");

        issues.MapPost("/", async (string key, IFormFileCollection files, UploadAttachmentsHandler handler, CancellationToken ct) =>
            {
                var incoming = new List<IncomingFile>();
                foreach (var file in files)
                {
                    if (file.Length > FileInspector.MaxBytes)
                    {
                        return new Application.Common.Error("validation", $"{file.FileName} is larger than 10 MB.", Application.Common.ErrorType.Validation)
                        {
                            Fields = new Dictionary<string, string[]> { ["files"] = [$"{file.FileName} is larger than 10 MB."] },
                        }.ToProblem();
                    }

                    using var buffer = new MemoryStream((int)file.Length);
                    await file.CopyToAsync(buffer, ct);
                    incoming.Add(new IncomingFile(file.FileName, buffer.ToArray()));
                }

                return (await handler.HandleAsync(new UploadAttachmentsCommand(key, incoming), ct))
                    .ToHttp(attachments => TypedResults.Created($"/api/issues/{key}/attachments", attachments));
            })
            .RequireAuthorization(Policies.TesterOrAdmin)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithSummary("Upload 1–5 files (multipart field \"files\"): images, PDF, XLSX, DOCX, TXT – max 10 MB each.");

        var attachments = app.MapGroup("/api/attachments").WithTags("Attachments");

        attachments.MapGet("/{id:int}", (int id, int? u, long? exp, string? sig, HttpContext http, OpenAttachmentHandler handler, CancellationToken ct) =>
                OpenAsync(id, thumbnail: false, u, exp, sig, http, handler, ct))
            .AllowAnonymous();

        attachments.MapGet("/{id:int}/thumbnail", (int id, int? u, long? exp, string? sig, HttpContext http, OpenAttachmentHandler handler, CancellationToken ct) =>
                OpenAsync(id, thumbnail: true, u, exp, sig, http, handler, ct))
            .AllowAnonymous();

        attachments.MapDelete("/{id:int}", async (int id, DeleteAttachmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(id, ct)).ToHttp(_ => TypedResults.NoContent()))
            .RequireAuthorization(Policies.TesterOrAdmin);

        return app;
    }

    private static async Task<IResult> OpenAsync(
        int id,
        bool thumbnail,
        int? userId,
        long? expires,
        string? signature,
        HttpContext http,
        OpenAttachmentHandler handler,
        CancellationToken ct)
    {
        var currentUser = http.RequestServices.GetRequiredService<ICurrentUser>();
        int? user;
        if (currentUser.IsAuthenticated)
        {
            user = currentUser.UserId;
        }
        else if (signature is null || userId is null || expires is null)
        {
            return TypedResults.Problem(
                detail: "Sign in, or use the link from the attachment list.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                extensions: new Dictionary<string, object?> { ["code"] = "attachment_unauthorized" });
        }
        else
        {
            user = http.RequestServices.GetRequiredService<IAttachmentUrlSigner>().Verify(id, thumbnail, userId.Value, expires.Value, signature);
        }

        if (user is null)
        {
            return TypedResults.Problem(
                detail: "This link is invalid or has expired. Reload the issue to get a new one.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                extensions: new Dictionary<string, object?> { ["code"] = "attachment_forbidden" });
        }

        var result = await handler.HandleAsync(new OpenAttachmentQuery(id, thumbnail, user.Value), ct);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblem();
        }

        var file = result.Value;
        var headers = http.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
        headers.CacheControl = "private, max-age=3600";
        headers.ContentDisposition = new ContentDisposition { Inline = file.Inline, FileName = file.FileName }.ToString();
        return TypedResults.Stream(file.Content, file.ContentType, enableRangeProcessing: true);
    }
}
