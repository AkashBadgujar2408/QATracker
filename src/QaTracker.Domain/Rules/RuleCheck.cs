namespace QaTracker.Domain.Rules;

/// <summary>Outcome of a business-rule check: allowed, or denied with a stable code and a user-safe reason.</summary>
public readonly record struct RuleCheck(bool Allowed, string? Code, string? Reason)
{
    public static RuleCheck Ok => new(true, null, null);

    public static RuleCheck Deny(string code, string reason) => new(false, code, reason);
}
