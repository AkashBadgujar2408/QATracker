using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using Severity = QaTracker.Domain.Enums.Severity;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Issues;

/// <param name="FoundOnEnvironmentId">Defaults to the sprint's default environment when omitted.</param>
public sealed record CreateIssueCommand(
    int SprintId,
    int ModuleId,
    string? ScreenName,
    int? FoundOnEnvironmentId,
    string? PageUrl,
    IssueType? Type,
    string Title,
    string? Steps,
    string? ExpectedResult,
    string ActualResult,
    string? Impact,
    Severity? Severity,
    Priority? Priority,
    int? AssigneeId) : IIssueFields;

public sealed class CreateIssueValidator : AbstractValidator<CreateIssueCommand>
{
    public CreateIssueValidator()
    {
        RuleFor(x => x.SprintId).GreaterThan(0);
        IssueRules.Apply(this);
    }
}

/// <summary>
/// Log an issue in a sprint that is in Testing (tester/admin). The number comes from the project's numbering lock
/// (PGERP-142); a Created activity – and an Assigned one for a pre-assignment – is written with it.
/// </summary>
public sealed class CreateIssueHandler(
    IValidator<CreateIssueCommand> validator,
    IApplicationDbContext db,
    IIssueNumbering numbering,
    IRichTextSanitizer sanitizer,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<IssueDto>> HandleAsync(CreateIssueCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var sprint = await db.TestingSprints.AsNoTracking()
            .Where(s => s.Id == command.SprintId)
            .Select(s => new { s.Id, s.ProjectId, s.Status, s.DefaultEnvironmentId, s.Project.ProjectKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (sprint is null)
        {
            return Error.NotFound("Sprint");
        }

        var check = SprintRules.CanChangeIssues(currentUser.Role, sprint.Status);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        var fields = command with { FoundOnEnvironmentId = command.FoundOnEnvironmentId ?? sprint.DefaultEnvironmentId };
        if (await IssueRules.CheckReferencesAsync(db, sprint.ProjectId, fields, null, cancellationToken) is { } badReference)
        {
            return badReference;
        }

        var actualResult = sanitizer.Sanitize(command.ActualResult);
        if (actualResult is null)
        {
            return Error.Validation(nameof(command.ActualResult), "Actual result is required.");
        }

        var now = clock.UtcNow;
        var issue = new Issue
        {
            ProjectId = sprint.ProjectId,
            SprintId = sprint.Id,
            ModuleId = fields.ModuleId,
            ScreenName = IssueRules.Clean(fields.ScreenName),
            FoundOnEnvironmentId = fields.FoundOnEnvironmentId,
            PageUrl = IssueRules.Clean(fields.PageUrl),
            Type = fields.Type!.Value,
            Title = fields.Title.Trim(),
            Steps = sanitizer.Sanitize(fields.Steps),
            ExpectedResult = sanitizer.Sanitize(fields.ExpectedResult),
            ActualResult = actualResult,
            Impact = sanitizer.Sanitize(fields.Impact),
            Severity = fields.Severity!.Value,
            Priority = fields.Priority!.Value,
            Status = fields.AssigneeId is null ? IssueStatus.New : IssueStatus.Assigned,
            ReporterId = currentUser.UserId,
            AssigneeId = fields.AssigneeId,
            ReopenCount = 0,
            CreatedAt = now,
        };
        issue.IssueActivities.Add(new IssueActivity { UserId = currentUser.UserId, Kind = ActivityKind.Created, CreatedAt = now });
        foreach (var watcher in new[] { currentUser.UserId, fields.AssigneeId }.OfType<int>().Distinct())
        {
            issue.IssueWatchers.Add(new IssueWatcher { UserId = watcher, AddedAt = now }); // reporter and assignee watch
        }

        if (fields.AssigneeId is { } assigneeId)
        {
            issue.IssueActivities.Add(new IssueActivity
            {
                UserId = currentUser.UserId,
                Kind = ActivityKind.Assigned,
                NewValue = assigneeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CreatedAt = now,
            });
        }

        await numbering.SaveWithNextNumberAsync(sprint.ProjectId, async number =>
        {
            issue.Number = number;
            issue.IssueKey = $"{sprint.ProjectKey}-{number}";
            db.Issues.Add(issue);
            await db.SaveChangesAsync(cancellationToken);
            return issue.Id;
        }, cancellationToken);

        var created = await db.Issues.AsNoTracking().Where(i => i.Id == issue.Id).Select(IssueDto.Projection).SingleAsync(cancellationToken);
        return await IssueAccess.WithAccessAsync(db, currentUser, created, cancellationToken);
    }
}

internal static class IssueAccess
{
    public static IssuePermissions For(ICurrentUser user, SprintStatus sprintStatus)
    {
        var canChange = SprintRules.CanChangeIssues(user.Role, sprintStatus).Allowed;
        return new IssuePermissions(canChange, canChange);
    }

    /// <summary>The issue with the current user's permissions and workflow actions (plan 7.2 allowedActions).</summary>
    public static async Task<IssueDto> WithAccessAsync(IApplicationDbContext db, ICurrentUser user, IssueDto issue, CancellationToken cancellationToken)
    {
        var flags = await db.Issues.AsNoTracking()
            .Where(i => i.Id == issue.Id)
            .Select(i => new
            {
                i.Project.RequireLiveVerification,
                HasPassedLiveRetest = i.Deployments.Any(d => d.Environment.Type == EnvironmentType.LIVE && d.RetestResult == RetestResult.Passed),
            })
            .SingleAsync(cancellationToken);

        var context = new WorkflowContext(
            issue.Status, issue.SprintStatus, issue.AssigneeId, user.Role, user.UserId, flags.RequireLiveVerification, flags.HasPassedLiveRetest);

        return issue with
        {
            Permissions = For(user, issue.SprintStatus),
            AllowedActions = IssueWorkflow.AllowedActions(context).Select(AllowedActionDto.From).ToList(),
        };
    }
}
