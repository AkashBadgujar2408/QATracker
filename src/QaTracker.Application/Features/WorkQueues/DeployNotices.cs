using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Application.Features.Issues;
using QaTracker.Application.Features.Workflow;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.WorkQueues;

/// <summary>Latest deployment of a candidate on one environment.</summary>
public sealed record CandidateDeploymentDto(int EnvironmentId, string EnvironmentName, EnvironmentType EnvironmentType, RetestResult RetestResult, DateTimeOffset DeployedAt);

/// <summary>One of the caller's fixed issues that can go into a deploy notice.</summary>
public sealed record DeployCandidateDto(WorkItemDto Issue, IReadOnlyList<CandidateDeploymentDto> Deployments);

public sealed record DeployNoticeIssueDto(int DeploymentId, int IssueId, string IssueKey, string Title, string? Version, RetestResult RetestResult);

/// <summary>A batch deploy notice (plan 7.4): one environment, one note, a deployment per issue.</summary>
public sealed record DeployNoticeDto(
    int Id,
    int ProjectId,
    string ProjectKey,
    string ProjectName,
    int EnvironmentId,
    string EnvironmentName,
    EnvironmentType EnvironmentType,
    string EnvironmentUrl,
    int CreatedById,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    string? Note,
    IReadOnlyList<DeployNoticeIssueDto> Issues)
{
    public static readonly Expression<Func<DeployNotice, DeployNoticeDto>> Projection = n => new DeployNoticeDto(
        n.Id,
        n.ProjectId,
        n.Project.ProjectKey,
        n.Project.Name,
        n.EnvironmentId,
        n.Environment.Name,
        n.Environment.Type,
        n.Environment.Url,
        n.CreatedById,
        n.CreatedBy.FullName,
        n.CreatedAt,
        n.Note,
        n.Deployments.OrderBy(d => d.Issue.Number)
            .Select(d => new DeployNoticeIssueDto(d.Id, d.IssueId, d.Issue.IssueKey, d.Issue.Title, d.Version, d.RetestResult))
            .ToList());
}

public sealed record DeployCandidatesQuery(int? ProjectId = null);

public sealed record ListDeployNoticesQuery(int? ProjectId = null, int Size = ListDeployNoticesQuery.DefaultSize)
{
    public const int DefaultSize = 20;
    public const int MaxSize = 100;
}

public sealed record CreateDeployNoticeCommand(int EnvironmentId, IReadOnlyList<string>? IssueKeys, string? Note, string? Version);

internal static class DeployNoticeRules
{
    public const int MaxIssues = 100;

    /// <summary>Fixes that can be announced: Fixed (another environment) or Verified (e.g. now on LIVE).</summary>
    public static readonly IReadOnlyList<IssueStatus> Announceable =
        IssueWorkflow.FromStatuses(IssueAction.MarkFixed).Where(s => s != IssueStatus.InProgress).ToList();
}

public sealed class ListDeployNoticesValidator : AbstractValidator<ListDeployNoticesQuery>
{
    public ListDeployNoticesValidator() => RuleFor(x => x.Size).InclusiveBetween(1, ListDeployNoticesQuery.MaxSize);
}

public sealed class CreateDeployNoticeValidator : AbstractValidator<CreateDeployNoticeCommand>
{
    public CreateDeployNoticeValidator()
    {
        RuleFor(x => x.EnvironmentId).GreaterThan(0).WithMessage("Choose the environment the fixes are deployed on.");
        RuleFor(x => x.IssueKeys).NotEmpty().WithMessage("Select at least one fixed issue.")
            .Must(keys => keys is null || keys.Count <= DeployNoticeRules.MaxIssues).WithMessage($"Announce at most {DeployNoticeRules.MaxIssues} issues at once.");
        RuleFor(x => x.Note).NotEmpty().WithMessage("Write a note for the tester.").MaximumLength(2000);
        RuleFor(x => x.Version).MaximumLength(100);
    }
}

/// <summary>The caller's fixed or verified issues (assigned to them, sprint testing completed) with their latest deployments.</summary>
public sealed class DeployCandidatesHandler(IApplicationDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<DeployCandidateDto>>> HandleAsync(DeployCandidatesQuery query, CancellationToken cancellationToken)
    {
        var me = currentUser.UserId;
        var announceable = DeployNoticeRules.Announceable.ToList();
        var issues = await WorkQueueQueries.Actionable(db, query.ProjectId)
            .Where(i => i.AssigneeId == me && announceable.Contains(i.Status))
            .OrderBy(i => i.ProjectId).ThenBy(i => i.Number)
            .Take(MyWorkHandler.MaxItems)
            .Select(WorkItemDto.Projection)
            .ToListAsync(cancellationToken);

        var ids = issues.Select(i => i.Id).ToList();
        var deployments = await db.Deployments.AsNoTracking()
            .Where(d => ids.Contains(d.IssueId))
            .OrderBy(d => d.Id)
            .Select(d => new { d.IssueId, Latest = new CandidateDeploymentDto(d.EnvironmentId, d.Environment.Name, d.Environment.Type, d.RetestResult, d.DeployedAt) })
            .ToListAsync(cancellationToken);

        return issues
            .Select(issue => new DeployCandidateDto(
                issue,
                deployments.Where(d => d.IssueId == issue.Id)
                    .GroupBy(d => d.Latest.EnvironmentId)
                    .Select(g => g.Last().Latest)
                    .ToList()))
            .ToList();
    }
}

/// <summary>Recent deploy notices, newest first.</summary>
public sealed class ListDeployNoticesHandler(IValidator<ListDeployNoticesQuery> validator, IApplicationDbContext db)
{
    public async Task<Result<IReadOnlyList<DeployNoticeDto>>> HandleAsync(ListDeployNoticesQuery query, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var notices = db.DeployNotices.AsNoTracking();
        if (query.ProjectId is { } projectId)
        {
            notices = notices.Where(n => n.ProjectId == projectId);
        }

        return await notices.OrderByDescending(n => n.Id).Take(query.Size).Select(DeployNoticeDto.Projection).ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Batch deploy notice (plan 7.4): the developer's own fixed issues of one project, deployed on one environment, one
/// note. Each issue gets a pending deployment and one Deployed timeline entry (Verified → Fixed again). All or nothing:
/// any issue that does not qualify rejects the notice with the reasons.
/// </summary>
public sealed class CreateDeployNoticeHandler(
    IValidator<CreateDeployNoticeCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    WorkflowSupport workflow)
{
    public async Task<Result<DeployNoticeDto>> HandleAsync(CreateDeployNoticeCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var environment = await db.ProjectEnvironments.AsNoTracking()
            .Where(e => e.Id == command.EnvironmentId && e.IsActive)
            .Select(e => new { e.Id, e.ProjectId, e.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (environment is null)
        {
            return Error.Validation("environmentId", "Choose an active environment.");
        }

        var keys = command.IssueKeys!.Select(IssueRules.NormalizeKey).Distinct().ToList();
        var issues = await db.Issues.Include(i => i.Sprint).Include(i => i.Project)
            .Where(i => keys.Contains(i.IssueKey))
            .ToDictionaryAsync(i => i.IssueKey, cancellationToken);
        var issueIds = issues.Values.Select(i => i.Id).ToList();
        var pendingHere = await db.Deployments.AsNoTracking()
            .Where(d => issueIds.Contains(d.IssueId) && d.EnvironmentId == environment.Id && d.RetestResult == RetestResult.Pending)
            .Select(d => d.IssueId)
            .ToListAsync(cancellationToken);

        var problems = new List<string>();
        foreach (var key in keys)
        {
            if (await ProblemAsync(issues.GetValueOrDefault(key), environment.ProjectId, pendingHere, environment.Name, cancellationToken) is { } problem)
            {
                problems.Add($"{key}: {problem}");
            }
        }

        if (problems.Count > 0)
        {
            return Error.Rule("not_deployable", string.Join(" ", problems));
        }

        var notice = new DeployNotice
        {
            ProjectId = environment.ProjectId,
            EnvironmentId = environment.Id,
            CreatedById = currentUser.UserId,
            CreatedAt = workflow.Now,
            Note = WorkflowSupport.Clean(command.Note),
        };
        db.DeployNotices.Add(notice);
        foreach (var issue in keys.Select(key => issues[key]))
        {
            workflow.Deployed(issue, environment.Id, command.Note, command.Version, notice);
            workflow.Fixed(issue);
            issue.UpdatedAt = workflow.Now;
        }

        if (await db.TrySaveAsync("One of the issues", cancellationToken) is { } saveError)
        {
            return saveError;
        }

        return await db.DeployNotices.AsNoTracking().Where(n => n.Id == notice.Id).Select(DeployNoticeDto.Projection).SingleAsync(cancellationToken);
    }

    private async Task<string?> ProblemAsync(Issue? issue, int projectId, List<int> pendingHere, string environmentName, CancellationToken cancellationToken)
    {
        if (issue is null)
        {
            return "not found.";
        }

        if (issue.ProjectId != projectId)
        {
            return "belongs to another project than the environment.";
        }

        if (issue.AssigneeId != currentUser.UserId)
        {
            return "is not assigned to you.";
        }

        if (!DeployNoticeRules.Announceable.Contains(issue.Status))
        {
            return "is not fixed yet – mark it fixed first.";
        }

        var check = await workflow.CheckAsync(issue, IssueAction.MarkFixed, cancellationToken);
        if (!check.Allowed)
        {
            return check.Reason;
        }

        return pendingHere.Contains(issue.Id) ? $"already waits for a retest on {environmentName}." : null;
    }
}
