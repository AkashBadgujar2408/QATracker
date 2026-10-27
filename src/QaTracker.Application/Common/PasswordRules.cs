using System.Security.Cryptography;
using System.Text;
using FluentValidation;

namespace QaTracker.Application.Common;

/// <summary>Password policy and helpers. Passwords are stored in plain text by decision (ADR-009).</summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    // No look-alike characters (0/O, 1/l/I) so temporary passwords can be read out or typed.
    private const string Letters = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";

    /// <summary>At least <see cref="MinLength"/> characters with a letter and a digit.</summary>
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty()
        .Length(MinLength, MaxLength)
        .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit))
        .WithMessage($"Password must be {MinLength}–{MaxLength} characters and contain a letter and a digit.");

    /// <summary>Constant-time comparison (no timing hints about how much of the password matched).</summary>
    public static bool Matches(string stored, string supplied) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(supplied));

    /// <summary>Random 12-character temporary password containing letters and digits.</summary>
    public static string GenerateTemporary()
    {
        const string all = Letters + Digits;
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        chars[RandomNumberGenerator.GetInt32(6)] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[6 + RandomNumberGenerator.GetInt32(6)] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        return new string(chars);
    }
}
