using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QaTracker.Api.Auth;
using QaTracker.Api.Common;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Attachments;
using QaTracker.Application.Features.Import;

namespace QaTracker.Api.Endpoints;

/// <summary>
/// Legacy Excel import (plan 14 IMPORT, Appendix C): preview parses the workbook into rows with warnings; commit sends
/// the same file again with the corrections from the preview grid and creates the issues in one transaction.
/// Tester/admin, into a sprint that is in Testing. Multipart fields: file, sprintId and (commit) corrections as JSON.
/// </summary>
public static class ImportEndpoints
{
    private const long MaxRequestBytes = LegacyImportReader.MaxWorkbookBytes + (1024 * 1024);

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var import = app.MapGroup("/api/import/excel")
            .WithTags("Import")
            .RequireAuthorization(Policies.TesterOrAdmin)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes));

        import.MapPost("/preview", async (IFormFile? file, [FromForm] int sprintId, ImportPreviewHandler handler, CancellationToken ct) =>
                await ReadAsync(file, ct) is { } workbook
                    ? (await handler.HandleAsync(new ImportPreviewCommand(sprintId, workbook), ct)).ToHttp(preview => TypedResults.Ok(preview))
                    : MissingFile())
            .WithSummary("Parse the legacy testing workbook: modules from sheets, one row per \"N. Title\" block with warnings.");

        import.MapPost("/commit", async (IFormFile? file, [FromForm] int sprintId, [FromForm] string? corrections, ImportCommitHandler handler, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json, CancellationToken ct) =>
            {
                if (await ReadAsync(file, ct) is not { } workbook)
                {
                    return MissingFile();
                }

                ImportCorrections? parsed;
                try
                {
                    parsed = string.IsNullOrWhiteSpace(corrections)
                        ? new ImportCorrections()
                        : JsonSerializer.Deserialize<ImportCorrections>(corrections, json.Value.SerializerOptions);
                }
                catch (JsonException)
                {
                    parsed = null;
                }

                if (parsed is null)
                {
                    return Error.Validation("corrections", "The corrections are not valid JSON.").ToProblem();
                }

                return (await handler.HandleAsync(new ImportCommitCommand(sprintId, workbook, parsed), ct)).ToHttp(result => TypedResults.Ok(result));
            })
            .WithSummary("Create the previewed issues (skips, renamed modules and row corrections from the preview applied).");

        return app;
    }

    private static async Task<IncomingFile?> ReadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        using var buffer = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(buffer, cancellationToken);
        return new IncomingFile(file.FileName, buffer.ToArray());
    }

    private static IResult MissingFile() => Error.Validation("file", "Choose the testing workbook (.xlsx).").ToProblem();
}
