namespace QaTracker.Api.Auth;

/// <summary>Authorization policies (plan S04). All except <see cref="SignedIn"/> also require a changed password.</summary>
public static class Policies
{
    /// <summary>Any signed-in user, even one who still has to change a temporary password (me, change-password, logout).</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>Signed in and password changed – the default for every endpoint.</summary>
    public const string Authenticated = "Authenticated";

    public const string TesterOrAdmin = "TesterOrAdmin";
    public const string DeveloperOrAdmin = "DeveloperOrAdmin";
    public const string AdminOnly = "AdminOnly";
}

/// <summary>Claims written into access tokens.</summary>
public static class QaClaims
{
    public const string UserId = "sub";
    public const string UserName = "unique_name";
    public const string FullName = "name";
    public const string Role = "role";
    public const string SecurityStamp = "stamp";
    public const string MustChangePassword = "pwd_change";
}

public static class RefreshCookie
{
    public const string Name = "qa_refresh";

    /// <summary>The cookie is only sent to the auth endpoints.</summary>
    public const string Path = "/api/auth";
}
