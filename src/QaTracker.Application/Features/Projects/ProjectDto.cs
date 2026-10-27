using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FluentValidation;
using QaTracker.Domain.Entities;

namespace QaTracker.Application.Features.Projects;

public sealed record ProjectDto(
    int Id,
    int ClientId,
    string ClientName,
    string Name,
    string Key,
    bool RequireLiveVerification,
    bool IsActive,
    int ModuleCount,
    int EnvironmentCount,
    int MemberCount,
    bool HasIssues,
    string RowVersion)
{
    public static readonly Expression<Func<Project, ProjectDto>> Projection = p => new ProjectDto(
        p.Id,
        p.ClientId,
        p.Client.Name,
        p.Name,
        p.ProjectKey,
        p.RequireLiveVerification,
        p.IsActive,
        p.Modules.Count(m => m.IsActive),
        p.ProjectEnvironments.Count(e => e.IsActive),
        p.ProjectMembers.Count,
        p.Issues.Any(),
        Convert.ToBase64String(p.RowVersion));
}

public static partial class ProjectKeys
{
    /// <summary>Issue key prefix (PGERP-142): 2–8 characters, upper-case letter first, then letters/digits.</summary>
    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);

    /// <summary>Keys are stored upper-case; "pgerp " is accepted as "PGERP".</summary>
    public static string Normalize(string key) => key.Trim().ToUpperInvariant();

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,7}$")]
    private static partial Regex Pattern();
}

public interface IProjectFields
{
    string Name { get; }
    string Key { get; }
}

internal static class ProjectRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : IProjectFields
    {
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        validator.RuleFor(x => x.Key).NotEmpty()
            .Must(key => ProjectKeys.IsValid(ProjectKeys.Normalize(key)))
            .WithMessage("Project key must be 2–8 characters: a letter first, then letters or digits (e.g. PGERP).");
    }
}
