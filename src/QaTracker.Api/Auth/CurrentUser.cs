using System.Globalization;
using QaTracker.Application.Abstractions;
using QaTracker.Domain.Enums;

namespace QaTracker.Api.Auth;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private HttpContext? Http => httpContextAccessor.HttpContext;

    public bool IsAuthenticated => Http?.User.Identity?.IsAuthenticated == true;

    public int UserId => int.Parse(
        Http?.User.FindFirst(QaClaims.UserId)?.Value ?? throw new InvalidOperationException("No signed-in user."),
        CultureInfo.InvariantCulture);

    public UserRole Role => Enum.Parse<UserRole>(
        Http?.User.FindFirst(QaClaims.Role)?.Value ?? throw new InvalidOperationException("No signed-in user."));

    public string? IpAddress => Http?.Connection.RemoteIpAddress?.ToString();
}
