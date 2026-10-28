using QaTracker.Application.Abstractions;
using SkiaSharp;

namespace QaTracker.Infrastructure.Files;

/// <summary>
/// WebP thumbnails (longest side 320 px) with SkiaSharp (MIT). Images over 50 megapixels are not decoded
/// (decompression bombs); EXIF orientation of photos is applied.
/// </summary>
public sealed class SkiaThumbnailGenerator : IThumbnailGenerator
{
    public const int MaxSide = 320;
    public const long MaxPixels = 50_000_000;
    private const int Quality = 80;

    public string Extension => ".webp";

    public string ContentType => "image/webp";

    public byte[]? Create(ReadOnlySpan<byte> image)
    {
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);
        if (codec is null || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            return null;
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return null;
        }

        using var oriented = Orient(decoded, codec.EncodedOrigin);
        var source = oriented ?? decoded;
        var scale = Math.Min(1d, (double)MaxSide / Math.Max(source.Width, source.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));

        using var resized = source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized is null)
        {
            return null;
        }

        using var encoded = resized.Encode(SKEncodedImageFormat.Webp, Quality);
        return encoded?.ToArray();
    }

    /// <summary>Upright copy for rotated phone photos; null when no rotation is needed.</summary>
    private static SKBitmap? Orient(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var (width, height, degrees) = origin switch
        {
            SKEncodedOrigin.RightTop => (bitmap.Height, bitmap.Width, 90f),
            SKEncodedOrigin.BottomRight => (bitmap.Width, bitmap.Height, 180f),
            SKEncodedOrigin.LeftBottom => (bitmap.Height, bitmap.Width, 270f),
            _ => (0, 0, 0f),
        };
        if (degrees == 0)
        {
            return null;
        }

        var rotated = new SKBitmap(width, height);
        using var canvas = new SKCanvas(rotated);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        return rotated;
    }
}
