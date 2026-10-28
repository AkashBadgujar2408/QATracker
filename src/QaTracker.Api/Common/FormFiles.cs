using QaTracker.Application.Common;
using QaTracker.Application.Features.Attachments;

namespace QaTracker.Api.Common;

/// <summary>Multipart files → <see cref="IncomingFile"/>s (each at most 10 MB, read into memory).</summary>
public static class FormFiles
{
    /// <summary>Request limit for an upload of five 10 MB files plus form fields.</summary>
    public const long MaxRequestBytes = (AttachmentWriter.MaxFilesPerUpload * FileInspector.MaxBytes) + (1024 * 1024);

    public static async Task<(List<IncomingFile> Files, IResult? Problem)> ReadAsync(IFormFileCollection? files, CancellationToken cancellationToken)
    {
        var incoming = new List<IncomingFile>();
        foreach (var file in files ?? new FormFileCollection())
        {
            if (file.Length > FileInspector.MaxBytes)
            {
                var message = $"{file.FileName} is larger than 10 MB.";
                return (incoming, new Error("validation", message, ErrorType.Validation)
                {
                    Fields = new Dictionary<string, string[]> { ["files"] = [message] },
                }.ToProblem());
            }

            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, cancellationToken);
            incoming.Add(new IncomingFile(file.FileName, buffer.ToArray()));
        }

        return (incoming, null);
    }
}
