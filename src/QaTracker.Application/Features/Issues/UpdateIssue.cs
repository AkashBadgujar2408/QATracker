using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using Severity = QaTracker.Domain.Enums.Severity;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Issues;

/// <param name="RowVersion">From the If-Match header – the version the user edited.</param>
public sealed record UpdateIssueCommand(
    string Key,
    string RowVersion,
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

public sealed class UpdateIssueValidator : AbstractValidator<UpdateIssueCommand>
{
    public UpdateIssueValidator()
    {
        RuleFor(x => x.RowVersion).ValidRowVersion();
        IssueRules.Apply(this);
    }
}

/// <summary>
/// Edit an issue's details while its sprint is in Testing (tester/admin). Every changed field writes a FieldChanged
/// activity, an assignee change an Assigned one; New ⇄ Assigned follows the assignee.
/// </summary>
public sealed class UpdateIssueHandler(
    IValidator<UpdateIssueCommand> validator,
    IApplicationDbContext db,
    IRichTextSanitizer sanitizer,
    ICurrentUser currentUser,
    IClock clock)
{
    private const int ValueMaxLength = 500;

    public async Task<Result<IssueDto>> HandleAsync(UpdateIssueCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var key = IssueRules.NormalizeKey(command.Key);
        var issue = await db.Issues.Include(i => i.Sprint).SingleOrDefaultAsync(i => i.IssueKey == key, cancellationToken);
        if (issue is null)
        {
            return Error.NotFound("Issue");
        }

        var check = SprintRules.CanChangeIssues(currentUser.Role, issue.Sprint.Status);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        if (await IssueRules.CheckReferencesAsync(db, issue.ProjectId, command, issue, cancellationToken) is { } badReference)
        {
            return badReference;
        }

        var actualResult = sanitizer.Sanitize(command.ActualResult);
        if (actualResult is null)
        {
            return Error.Validation(nameof(command.ActualResult), "Actual result is required.");
        }

        var now = clock.UtcNow;
        var changes = new Changes(issue, currentUser.UserId, now);
        changes.Set("Module", issue.ModuleId, command.ModuleId, v => issue.ModuleId = v);
        changes.Set("Screen", issue.ScreenName, IssueRules.Clean(command.ScreenName), v => issue.ScreenName = v);
        changes.Set("Found on environment", issue.FoundOnEnvironmentId, command.FoundOnEnvironmentId, v => issue.FoundOnEnvironmentId = v);
        changes.Set("Page URL", issue.PageUrl, IssueRules.Clean(command.PageUrl), v => issue.PageUrl = v);
        changes.Set("Type", issue.Type, command.Type!.Value, v => issue.Type = v);
        changes.Set("Title", issue.Title, command.Title.Trim(), v => issue.Title = v);
        changes.SetRichText("Steps", issue.Steps, sanitizer.Sanitize(command.Steps), v => issue.Steps = v);
        changes.SetRichText("Expected result", issue.ExpectedResult, sanitizer.Sanitize(command.ExpectedResult), v => issue.ExpectedResult = v);
        changes.SetRichText("Actual result", issue.ActualResult, actualResult, v => issue.ActualResult = v!);
        changes.SetRichText("Impact", issue.Impact, sanitizer.Sanitize(command.Impact), v => issue.Impact = v);
        changes.Set("Severity", issue.Severity, command.Severity!.Value, v => issue.Severity = v);
        changes.Set("Priority", issue.Priority, command.Priority!.Value, v => issue.Priority = v);

        if (command.AssigneeId != issue.AssigneeId)
        {
            db.IssueActivities.Add(new IssueActivity
            {
                IssueId = issue.Id,
                UserId = currentUser.UserId,
                Kind = ActivityKind.Assigned,
                OldValue = issue.AssigneeId?.ToString(CultureInfo.InvariantCulture),
                NewValue = command.AssigneeId?.ToString(CultureInfo.InvariantCulture),
                CreatedAt = now,
            });
            issue.AssigneeId = command.AssigneeId;
            issue.Status = issue.Status switch
            {
                IssueStatus.New when command.AssigneeId is not null => IssueStatus.Assigned,
                IssueStatus.Assigned when command.AssigneeId is null => IssueStatus.New,
                _ => issue.Status,
            };
        }

        foreach (var activity in changes.Activities)
        {
            db.IssueActivities.Add(activity);
        }

        db.ExpectRowVersion(issue, command.RowVersion);
        if (db.Entry(issue).State == EntityState.Modified)
        {
            issue.UpdatedAt = now;
        }

        if (await db.TrySaveAsync("This issue", cancellationToken) is { } failed)
        {
            return failed;
        }

        var updated = await db.Issues.AsNoTracking().Where(i => i.Id == issue.Id).Select(IssueDto.Projection).SingleAsync(cancellationToken);
        return await IssueAccess.WithAccessAsync(db, currentUser, updated, cancellationToken);
    }

    /// <summary>Applies changed values and collects one FieldChanged activity per change.</summary>
    private sealed class Changes(Issue issue, int userId, DateTimeOffset now)
    {
        public List<IssueActivity> Activities { get; } = [];

        public void Set<T>(string field, T current, T wanted, Action<T> apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted))
            {
                return;
            }

            apply(wanted);
            Add(field, Format(current), Format(wanted));
        }

        /// <summary>Long HTML is not copied into the timeline – only that it was edited.</summary>
        public void SetRichText(string field, string? current, string? wanted, Action<string?> apply)
        {
            if (current == wanted)
            {
                return;
            }

            apply(wanted);
            Add(field, null, null);
        }

        private void Add(string field, string? oldValue, string? newValue) => Activities.Add(new IssueActivity
        {
            IssueId = issue.Id,
            UserId = userId,
            Kind = ActivityKind.FieldChanged,
            Body = field,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = now,
        });

        private static string? Format<T>(T value)
        {
            var text = value switch
            {
                null => null,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            return text is { Length: > ValueMaxLength } ? text[..ValueMaxLength] : text;
        }
    }
}
