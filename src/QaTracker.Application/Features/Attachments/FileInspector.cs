using System.IO.Compression;
using System.Text;

namespace QaTracker.Application.Features.Attachments;

/// <summary>An accepted file kind: what the content proves it is, not what the client claims.</summary>
public sealed record FileKind(string ContentType, string Extension, bool IsImage);

/// <summary>
/// Allowed attachments (plan 7.1): images (PNG, JPEG, GIF, WebP), PDF, XLSX, DOCX, TXT – max 10 MB each.
/// The type is detected from the content (magic bytes; Office files must contain their main part; text must be UTF-8
/// without NUL characters) and the file name's extension must agree.
/// </summary>
public static class FileInspector
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    public static readonly FileKind Png = new("image/png", ".png", true);
    public static readonly FileKind Jpeg = new("image/jpeg", ".jpg", true);
    public static readonly FileKind Gif = new("image/gif", ".gif", true);
    public static readonly FileKind Webp = new("image/webp", ".webp", true);
    public static readonly FileKind Pdf = new("application/pdf", ".pdf", false);
    public static readonly FileKind Xlsx = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx", false);
    public static readonly FileKind Docx = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx", false);
    public static readonly FileKind Text = new("text/plain", ".txt", false);

    private static readonly IReadOnlyDictionary<string, FileKind> ByExtension = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = Png,
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
        [".gif"] = Gif,
        [".webp"] = Webp,
        [".pdf"] = Pdf,
        [".xlsx"] = Xlsx,
        [".docx"] = Docx,
        [".txt"] = Text,
        [".log"] = Text,
    };

    public static string AllowedExtensions => string.Join(", ", ByExtension.Keys);

    /// <summary>The kind of the file, or an error message for the user.</summary>
    public static (FileKind? Kind, string? Error) Inspect(string fileName, ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
        {
            return (null, $"{fileName} is empty.");
        }

        if (content.Length > MaxBytes)
        {
            return (null, $"{fileName} is larger than 10 MB.");
        }

        if (!ByExtension.TryGetValue(Path.GetExtension(fileName), out var claimed))
        {
            return (null, $"{fileName}: only {AllowedExtensions} files can be attached.");
        }

        var detected = Detect(content);
        return detected == claimed
            ? (detected, null)
            : (null, $"{fileName} does not look like a {claimed.Extension.TrimStart('.').ToUpperInvariant()} file.");
    }

    private static FileKind? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PngSignature))
        {
            return Png;
        }

        if (content.StartsWith(JpegSignature))
        {
            return Jpeg;
        }

        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8))
        {
            return Gif;
        }

        if (content.Length >= 12 && content.StartsWith("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        if (content.StartsWith("%PDF-"u8))
        {
            return Pdf;
        }

        if (content.StartsWith("PK\x03\x04"u8))
        {
            return OfficeKind(content);
        }

        return IsText(content) ? Text : null;
    }

    /// <summary>A zip is only accepted when it is a Word or Excel document.</summary>
    private static FileKind? OfficeKind(ReadOnlySpan<byte> content)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content.ToArray()), ZipArchiveMode.Read);
            var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!names.Contains("[Content_Types].xml"))
            {
                return null;
            }

            return names.Contains("word/document.xml") ? Docx : names.Contains("xl/workbook.xml") ? Xlsx : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool IsText(ReadOnlySpan<byte> content)
    {
        if (content.Contains((byte)0))
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetCharCount(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>A safe display name: no folders, no control or path characters, at most 255 characters.</summary>
    public static string CleanFileName(string fileName, FileKind kind)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Where(ch => !char.IsControl(ch) && !invalid.Contains(ch)).ToArray()).Trim();
        if (Path.GetFileNameWithoutExtension(name).Length == 0)
        {
            name = "attachment" + kind.Extension;
        }

        return name.Length <= 255 ? name : name[..(255 - Path.GetExtension(name).Length)] + Path.GetExtension(name);
    }
}
