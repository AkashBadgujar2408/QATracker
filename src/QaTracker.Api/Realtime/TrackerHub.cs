using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using QaTracker.Api.Auth;

namespace QaTracker.Api.Realtime;

/// <summary>
/// Live updates (plan 14 SIGNALR, ADR-028) at <c>/hubs/tracker</c>. Server → client only: IssueChanged, ActivityAdded,
/// SprintChanged, NotificationCreated, NotificationsRead. Every connection joins its user's group; an open issue page
/// joins the issue's group for its timeline. Same sign-in rules as the API (default policy).
/// </summary>
public sealed class TrackerHub : Hub
{
    public const string Path = "/hubs/tracker";

    public static string UserGroup(int userId) => $"user:{userId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Keys are compared upper-case (as IssueRules.NormalizeKey does).</summary>
    public static string IssueGroup(string issueKey) => $"issue:{issueKey.Trim().ToUpperInvariant()}";

    public override async Task OnConnectedAsync()
    {
        if (int.TryParse(Context.User?.FindFirst(QaClaims.UserId)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Every signed-in user may read every issue, so joining needs no further check.</summary>
    public Task WatchIssue(string issueKey) => Groups.AddToGroupAsync(Context.ConnectionId, IssueGroup(issueKey));

    public Task UnwatchIssue(string issueKey) => Groups.RemoveFromGroupAsync(Context.ConnectionId, IssueGroup(issueKey));
}
