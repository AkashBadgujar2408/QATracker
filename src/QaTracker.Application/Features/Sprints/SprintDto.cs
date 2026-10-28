using System.Linq.Expressions;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;

namespace QaTracker.Application.Features.Sprints;

/// <param name="OpenIssueCount">Issues that are neither Closed nor Rejected.</param>
public sealed record SprintDto(
    int Id,
    int ProjectId,
    string ProjectKey,
    string ProjectName,
    int ClientId,
    string ClientName,
    string Name,
    int PhaseId,
    string PhaseName,
    int? DefaultEnvironmentId,
    string? DefaultEnvironmentName,
    SprintStatus Status,
    DateTimeOffset StartedAt,
    string StartedByName,
    DateTimeOffset? TestingCompletedAt,
    string? TestingCompletedByName,
    DateTimeOffset? ClosedAt,
    int IssueCount,
    int OpenIssueCount,
    string RowVersion)
{
    public static readonly Expression<Func<TestingSprint, SprintDto>> Projection = s => new SprintDto(
        s.Id,
        s.ProjectId,
        s.Project.ProjectKey,
        s.Project.Name,
        s.Project.ClientId,
        s.Project.Client.Name,
        s.Name,
        s.PhaseId,
        s.Phase.Name,
        s.DefaultEnvironmentId,
        s.DefaultEnvironment != null ? s.DefaultEnvironment.Name : null,
        s.Status,
        s.StartedAt,
        s.StartedBy.FullName,
        s.TestingCompletedAt,
        s.TestingCompletedBy != null ? s.TestingCompletedBy.FullName : null,
        s.ClosedAt,
        s.IssueSprints.Count,
        s.IssueSprints.Count(i => i.Status != IssueStatus.Closed && i.Status != IssueStatus.Rejected),
        Convert.ToBase64String(s.RowVersion));
}

/// <summary>One entry of the sprint's own history (plan 6.1).</summary>
public sealed record SprintEventDto(SprintEventKind Kind, string UserName, string? Note, DateTimeOffset At);

public sealed record SprintDetailDto(SprintDto Sprint, IReadOnlyList<SprintEventDto> Events);
