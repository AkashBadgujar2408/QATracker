using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Projects;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Lookups;

public sealed record LookupItem(int Id, string Name);

public sealed record ProjectLookup(int Id, string Name, string Key);

public sealed record EnvironmentLookup(int Id, string Name, EnvironmentType Type, string Url);

public sealed record MemberLookup(int UserId, string FullName, UserRole Role);

/// <summary>Everything the Testing screen pickers need in one call (active items only).</summary>
public sealed record CascadeDto(
    IReadOnlyList<LookupItem> Clients,
    IReadOnlyList<ProjectLookup> Projects,
    IReadOnlyList<LookupItem> Modules,
    IReadOnlyList<EnvironmentLookup> Environments,
    IReadOnlyList<MemberLookup> Members);

public sealed record CascadeQuery(int? ClientId, int? ProjectId);

/// <summary>Clients always; projects for <c>ClientId</c>; modules, environments and members for <c>ProjectId</c>.</summary>
public sealed class CascadeHandler(IApplicationDbContext db)
{
    public async Task<CascadeDto> HandleAsync(CascadeQuery query, CancellationToken cancellationToken)
    {
        var clients = await db.Clients.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(cancellationToken);

        var projects = query.ClientId is { } clientId
            ? await db.Projects.AsNoTracking().Where(p => p.ClientId == clientId && p.IsActive).OrderBy(p => p.Name)
                .Select(p => new ProjectLookup(p.Id, p.Name, p.ProjectKey)).ToListAsync(cancellationToken)
            : [];

        if (query.ProjectId is not { } projectId)
        {
            return new CascadeDto(clients, projects, [], [], []);
        }

        var modules = await db.Modules.AsNoTracking().Where(m => m.ProjectId == projectId && m.IsActive).OrderBy(m => m.Name)
            .Select(m => new LookupItem(m.Id, m.Name)).ToListAsync(cancellationToken);
        var environments = await db.ProjectEnvironments.AsNoTracking().Where(e => e.ProjectId == projectId && e.IsActive)
            .OrderBy(e => e.Type).ThenBy(e => e.Name)
            .Select(e => new EnvironmentLookup(e.Id, e.Name, e.Type, e.Url)).ToListAsync(cancellationToken);
        var members = await db.ProjectMembers.AsNoTracking().Where(m => m.ProjectId == projectId && m.User.IsActive)
            .OrderBy(m => m.User.FullName)
            .Select(m => new MemberLookup(m.UserId, m.User.FullName, m.User.Role)).ToListAsync(cancellationToken);

        return new CascadeDto(clients, projects, modules, environments, members);
    }
}

public enum MasterKind
{
    Client,
    Project,
    Module,
    Environment,
}

/// <param name="Exact">Same name ignoring case, spaces and punctuation – creating it would be a duplicate.</param>
public sealed record SimilarName(int Id, string Name, bool Exact, bool IsActive);

/// <param name="ParentId">Client for projects, project for modules and environments.</param>
public sealed record SimilarNamesQuery(MasterKind Kind, string Name, int? ParentId);

/// <summary>"Did you mean…?" for quick-add dialogs: existing names in the same scope that look alike (plan 8.1).</summary>
public sealed class SimilarNamesHandler(IApplicationDbContext db)
{
    public const int MaxResults = 5;

    public async Task<Result<IReadOnlyList<SimilarName>>> HandleAsync(SimilarNamesQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Name))
        {
            return Array.Empty<SimilarName>();
        }

        if (query.Kind != MasterKind.Client && query.ParentId is null)
        {
            return Error.Validation(nameof(query.ParentId), $"{query.Kind} names are checked within a parent – parentId is required.");
        }

        var parentId = query.ParentId ?? 0;
        var candidates = query.Kind switch
        {
            MasterKind.Client => await db.Clients.AsNoTracking().Select(c => new SimilarName(c.Id, c.Name, false, c.IsActive)).ToListAsync(cancellationToken),
            MasterKind.Project => await db.Projects.AsNoTracking().Where(p => p.ClientId == parentId).Select(p => new SimilarName(p.Id, p.Name, false, p.IsActive)).ToListAsync(cancellationToken),
            MasterKind.Module => await db.Modules.AsNoTracking().Where(m => m.ProjectId == parentId).Select(m => new SimilarName(m.Id, m.Name, false, m.IsActive)).ToListAsync(cancellationToken),
            _ => await db.ProjectEnvironments.AsNoTracking().Where(e => e.ProjectId == parentId).Select(e => new SimilarName(e.Id, e.Name, false, e.IsActive)).ToListAsync(cancellationToken),
        };

        return NameSimilarity.Rank(query.Name, candidates).Take(MaxResults).ToList();
    }
}

public static class NameSimilarity
{
    /// <summary>Lower-case letters and digits only: "Sales & Distribution" → "salesdistribution".</summary>
    public static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    /// <summary>Exact matches first, then containment, then small edit distance (typos).</summary>
    public static IEnumerable<SimilarName> Rank(string name, IEnumerable<SimilarName> candidates)
    {
        var wanted = Normalize(name);
        if (wanted.Length == 0)
        {
            return [];
        }

        return candidates
            .Select(c => (Candidate: c, Score: Score(wanted, Normalize(c.Name))))
            .Where(x => x.Score is not null)
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Candidate with { Exact = x.Score == 0 });
    }

    /// <summary>0 = exact, 1 = one contains the other, 2+ = edit distance + 1; null = not similar.</summary>
    private static int? Score(string wanted, string existing)
    {
        if (existing.Length == 0)
        {
            return null;
        }

        if (wanted == existing)
        {
            return 0;
        }

        var shorter = Math.Min(wanted.Length, existing.Length);
        if (shorter >= 3 && (existing.Contains(wanted, StringComparison.Ordinal) || wanted.Contains(existing, StringComparison.Ordinal)))
        {
            return 1;
        }

        var allowed = Math.Max(1, Math.Max(wanted.Length, existing.Length) / 4);
        var distance = Levenshtein(wanted, existing, allowed);
        return distance <= allowed ? distance + 1 : null;
    }

    /// <summary>Edit distance, stopping early once it exceeds <paramref name="limit"/>.</summary>
    private static int Levenshtein(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit)
        {
            return limit + 1;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}

public sealed record ProjectKeySuggestionQuery(int ClientId, string ProjectName);

public sealed record ProjectKeySuggestion(string Key);

/// <summary>Suggests a free project key: client initials + project initials/short name ("Pardi Gears" + "ERP" → PGERP).</summary>
public sealed class SuggestProjectKeyHandler(IApplicationDbContext db)
{
    public async Task<Result<ProjectKeySuggestion>> HandleAsync(ProjectKeySuggestionQuery query, CancellationToken cancellationToken)
    {
        var client = await db.Clients.AsNoTracking().Where(c => c.Id == query.ClientId).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken);
        if (client is null)
        {
            return Error.NotFound("Client");
        }

        var baseKey = ProjectKeySuggester.BaseKey(client, query.ProjectName);
        // Projects are few (hundreds at most), so load every key – suffixed candidates may be shorter than the base.
        var taken = await db.Projects.AsNoTracking().Select(p => p.ProjectKey).ToListAsync(cancellationToken);

        return new ProjectKeySuggestion(ProjectKeySuggester.FirstFree(baseKey, taken.ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }
}

public static class ProjectKeySuggester
{
    public static string BaseKey(string clientName, string projectName)
    {
        var clientPart = string.Concat(Words(clientName).Take(3).Select(w => w[0]));
        var projectWords = Words(projectName).ToList();
        var projectPart = projectWords.Count switch
        {
            0 => string.Empty,
            1 => projectWords[0][..Math.Min(5, projectWords[0].Length)],
            _ => string.Concat(projectWords.Take(4).Select(w => w[0])),
        };

        var key = (clientPart + projectPart).ToUpperInvariant();
        key = new string(key.SkipWhile(ch => !char.IsAsciiLetter(ch)).ToArray());
        key = key.Length > 8 ? key[..8] : key;
        return key.Length >= 2 ? key : (key + "PRJ")[..Math.Min(8, key.Length + 3)];
    }

    /// <summary><paramref name="baseKey"/> itself, else with a number suffix (PGERP2, PGERP3 …), within 8 characters.</summary>
    public static string FirstFree(string baseKey, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(baseKey))
        {
            return baseKey;
        }

        for (var n = 2; n < 1000; n++)
        {
            var suffix = n.ToString(CultureInfo.InvariantCulture);
            var candidate = baseKey[..Math.Min(baseKey.Length, 8 - suffix.Length)] + suffix;
            if (!taken.Contains(candidate) && ProjectKeys.IsValid(candidate))
            {
                return candidate;
            }
        }

        return baseKey;
    }

    private static IEnumerable<string> Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsAsciiLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0);
}

public sealed record UserLookup(int Id, string UserName, string FullName, UserRole Role, string? AvatarColor);

/// <summary>Active users for member and assignee pickers (testers cannot read the admin-only users list).</summary>
public sealed class UserLookupHandler(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<UserLookup>> HandleAsync(UserRole? role, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking()
            .Where(u => u.IsActive && (role == null || u.Role == role))
            .OrderBy(u => u.FullName)
            .Select(u => new UserLookup(u.Id, u.UserName, u.FullName, u.Role, u.AvatarColor))
            .ToListAsync(cancellationToken);
}
