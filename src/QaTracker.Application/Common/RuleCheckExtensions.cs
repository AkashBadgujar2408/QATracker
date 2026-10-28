using QaTracker.Domain.Rules;

namespace QaTracker.Application.Common;

public static class RuleCheckExtensions
{
    /// <summary>A denied domain rule as an API error: a role problem is 403, anything else 422.</summary>
    public static Error ToError(this RuleCheck check) =>
        check.Code == "role_not_allowed"
            ? Error.Forbidden(check.Code, check.Reason!)
            : Error.Rule(check.Code!, check.Reason!);
}
