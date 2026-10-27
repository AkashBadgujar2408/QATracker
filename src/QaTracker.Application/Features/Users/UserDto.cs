using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Users;

/// <summary>User as shown in Admin › Users. Never contains the password.</summary>
public sealed record UserDto(
    int Id,
    string UserName,
    string FullName,
    string Email,
    UserRole Role,
    bool IsActive,
    bool MustChangePassword,
    string? AvatarColor,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    string RowVersion)
{
    public static readonly Expression<Func<User, UserDto>> Projection = u => new UserDto(
        u.Id,
        u.UserName,
        u.FullName,
        u.Email,
        u.Role,
        u.IsActive,
        u.MustChangePassword,
        u.AvatarColor,
        u.LastLoginAt,
        u.CreatedAt,
        Convert.ToBase64String(u.RowVersion));

    private static readonly Func<User, UserDto> FromEntity = Projection.Compile();

    public static UserDto From(User user) => FromEntity(user);
}

/// <summary>A user plus the one-time temporary password (create / reset). Show it once; it is not returned again.</summary>
public sealed record UserWithPasswordDto(UserDto User, string TemporaryPassword);

/// <summary>Shared field rules for create and update.</summary>
internal static class UserRules
{
    public const string UserNamePattern = "^[A-Za-z0-9._-]{3,50}$";
    public const string AvatarColorPattern = "^#[0-9A-Fa-f]{6}$";

    public static void FullNameEmailRoleColor<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, string>> fullName,
        Expression<Func<T, string>> email,
        Expression<Func<T, UserRole>> role,
        Expression<Func<T, string?>> avatarColor)
    {
        validator.RuleFor(fullName).NotEmpty().MaximumLength(100);
        validator.RuleFor(email).NotEmpty().MaximumLength(256).EmailAddress();
        validator.RuleFor(role).IsInEnum();
        // Null/empty = colour derived from the name in the UI.
        validator.RuleFor(avatarColor).Matches(AvatarColorPattern).WithMessage("Avatar colour must look like #4F46E5.");
    }

    /// <summary>True when <paramref name="userId"/> is the only active admin (who must keep the role and stay active).</summary>
    public static async Task<bool> IsLastActiveAdminAsync(IApplicationDbContext db, int userId, CancellationToken cancellationToken) =>
        !await db.Users.AnyAsync(u => u.Id != userId && u.Role == UserRole.Admin && u.IsActive, cancellationToken)
        && await db.Users.AnyAsync(u => u.Id == userId && u.Role == UserRole.Admin && u.IsActive, cancellationToken);
}
