using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Application.Features.Auth;

public sealed class GetMeHandler(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<CurrentUserDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);
        return user is null ? Error.NotFound("User") : CurrentUserDto.From(user);
    }
}
