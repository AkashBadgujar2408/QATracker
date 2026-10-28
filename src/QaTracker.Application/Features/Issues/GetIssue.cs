using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;

namespace QaTracker.Application.Features.Issues;

/// <summary>An issue by key ("PGERP-142", case-insensitive) with what the current user may do with it.</summary>
public sealed class GetIssueHandler(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<IssueDto>> HandleAsync(string key, CancellationToken cancellationToken)
    {
        var normalized = IssueRules.NormalizeKey(key);
        var issue = await db.Issues.AsNoTracking().Where(i => i.IssueKey == normalized).Select(IssueDto.Projection).SingleOrDefaultAsync(cancellationToken);
        return issue is null
            ? Error.NotFound("Issue")
            : await IssueAccess.WithAccessAsync(db, currentUser, issue, cancellationToken);
    }
}

public sealed record ScreenSuggestionQuery(int ProjectId, int? ModuleId, string? Search);

/// <summary>Screen names already used in the project (the module's first), most used first – plan 7.1 "suggestions".</summary>
public sealed class ScreenSuggestionsHandler(IApplicationDbContext db)
{
    public const int MaxResults = 10;

    public async Task<Result<IReadOnlyList<string>>> HandleAsync(ScreenSuggestionQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == query.ProjectId, cancellationToken))
        {
            return Error.NotFound("Project");
        }

        var issues = db.Issues.AsNoTracking().Where(i => i.ProjectId == query.ProjectId && i.ScreenName != null);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            issues = issues.Where(i => i.ScreenName!.Contains(search));
        }

        var moduleId = query.ModuleId ?? 0;
        return await issues
            .GroupBy(i => i.ScreenName!)
            .Select(g => new { Name = g.Key, InModule = g.Any(i => i.ModuleId == moduleId), Uses = g.Count() })
            .OrderByDescending(x => x.InModule).ThenByDescending(x => x.Uses).ThenBy(x => x.Name)
            .Select(x => x.Name)
            .Take(MaxResults)
            .ToListAsync(cancellationToken);
    }
}
