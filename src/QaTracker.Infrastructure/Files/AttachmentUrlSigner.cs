using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Infrastructure.Files;

/// <summary>
/// HMAC-SHA256 over attachment id, kind, user and expiry, with a key derived from Auth:SigningKey.
/// URLs live for <see cref="Lifetime"/>; lists hand out fresh ones on every load.
/// </summary>
public sealed class AttachmentUrlSigner(IOptions<AuthOptions> options, IClock clock) : IAttachmentUrlSigner
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes("qa-tracker:attachments:" + options.Value.SigningKey));

    public string Sign(int attachmentId, bool thumbnail, int userId)
    {
        var expires = clock.UtcNow.Add(Lifetime).ToUnixTimeSeconds();
        var signature = Signature(attachmentId, thumbnail, userId, expires);
        var path = thumbnail ? $"/api/attachments/{attachmentId}/thumbnail" : $"/api/attachments/{attachmentId}";
        return string.Create(CultureInfo.InvariantCulture, $"{path}?u={userId}&exp={expires}&sig={signature}");
    }

    public int? Verify(int attachmentId, bool thumbnail, int userId, long expires, string signature)
    {
        if (expires < clock.UtcNow.ToUnixTimeSeconds())
        {
            return null;
        }

        var expected = Encoding.ASCII.GetBytes(Signature(attachmentId, thumbnail, userId, expires));
        var actual = Encoding.ASCII.GetBytes(signature);
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? userId : null;
    }

    private string Signature(int attachmentId, bool thumbnail, int userId, long expires)
    {
        var message = string.Create(CultureInfo.InvariantCulture, $"{attachmentId}:{(thumbnail ? 't' : 'f')}:{userId}:{expires}");
        return Base64Url.EncodeToString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(message)));
    }
}
