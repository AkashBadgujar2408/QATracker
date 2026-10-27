using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Users;

public sealed record ListUsersQuery(string? Search, UserRole? Role, bool? IsActive);

public sealed class ListUsersHandler(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<UserDto>> HandleAsync(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            users = users.Where(u => u.FullName.Contains(search) || u.UserName.Contains(search) || u.Email.Contains(search));
        }

        if (query.Role is { } role)
        {
            users = users.Where(u => u.Role == role);
        }

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        return await users.OrderBy(u => u.FullName).Select(UserDto.Projection).ToListAsync(cancellationToken);
    }
}
