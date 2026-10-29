using SkiaSharp;

namespace QaTracker.Infrastructure.Reports;

/// <summary>Thumbnails are stored as WebP; Excel needs PNG and the PDF writer draws decoded images.</summary>
internal static class ReportImages
{
    public sealed record Png(byte[] Bytes, int Width, int Height);

    public static Png? ToPng(byte[] image)
    {
        using var decoded = Decode(image);
        if (decoded is null)
        {
            return null;
        }

        using var data = decoded.Encode(SKEncodedImageFormat.Png, 100);
        return data is null ? null : new Png(data.ToArray(), decoded.Width, decoded.Height);
    }

    /// <summary>The decoded image, or null when the bytes are not an image Skia can read.</summary>
    public static SKImage? Decode(byte[] image)
    {
        using var data = SKData.CreateCopy(image);
        return SKImage.FromEncodedData(data);
    }
}
