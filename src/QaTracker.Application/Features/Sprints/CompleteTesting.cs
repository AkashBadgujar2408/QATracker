using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using QaTracker.Domain.Rules;

namespace QaTracker.Application.Features.Sprints;

public sealed record CompleteTestingCommand(int SprintId, string RowVersion, string? Note = null);

public sealed class CompleteTestingValidator : AbstractValidator<CompleteTestingCommand>
{
    public CompleteTestingValidator()
    {
        RuleFor(x => x.RowVersion).ValidRowVersion();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

/// <summary>
/// "Mark Testing Completed" (plan 6): issues get due dates from the SLA (ADR-016), each issue's timeline and the sprint
/// history record it, and project members plus assignees get an in-app notification. One transaction.
/// </summary>
public sealed class CompleteTestingHandler(
    IValidator<CompleteTestingCommand> validator,
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IClock clock)
{
    public async Task<Result<SprintDto>> HandleAsync(CompleteTestingCommand command, CancellationToken cancellationToken)
    {
        if (await validator.CheckAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var sprint = await db.TestingSprints.Include(s => s.Project).SingleOrDefaultAsync(s => s.Id == command.SprintId, cancellationToken);
        if (sprint is null)
        {
            return Error.NotFound("Sprint");
        }

        var check = SprintRules.CanCompleteTesting(currentUser.Role, sprint.Status);
        if (!check.Allowed)
        {
            return check.ToError();
        }

        var now = clock.UtcNow;
        db.ExpectRowVersion(sprint, command.RowVersion);
        sprint.Status = SprintStatus.TestingCompleted;
        sprint.TestingCompletedAt = now;
        sprint.TestingCompletedById = currentUser.UserId;
        db.SprintEvents.Add(new SprintEvent
        {
            SprintId = sprint.Id,
            UserId = currentUser.UserId,
            Kind = SprintEventKind.TestingCompleted,
            Note = SprintText.Clean(command.Note),
            At = now,
        });

        var issues = await db.Issues.Where(i => i.SprintId == sprint.Id).ToListAsync(cancellationToken);
        await SetDueDatesAsync(sprint, issues, now, cancellationToken);
        await NotifyAsync(sprint, issues, now, cancellationToken);

        if (await db.TrySaveAsync("This sprint", cancellationToken) is { } failed)
        {
            return failed;
        }

        return await db.TestingSprints.AsNoTracking().Where(s => s.Id == sprint.Id).Select(SprintDto.Projection).SingleAsync(cancellationToken);
    }

    private async Task SetDueDatesAsync(TestingSprint sprint, List<Issue> issues, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
        {
            return;
        }

        var sla = await db.SlaPolicies.AsNoTracking().ToDictionaryAsync(p => p.Severity, p => p.WorkingHours, cancellationToken);
        var today = DateOnly.FromDateTime(now.ToOffset(WorkingCalendar.Ist).DateTime);
        var holidays = (await db.Holidays.AsNoTracking().Where(h => h.Date >= today).Select(h => h.Date).ToListAsync(cancellationToken)).ToHashSet();

        foreach (var issue in issues.Where(i => !SprintRules.IsFinished(i.Status)))
        {
            var due = sla.TryGetValue(issue.Severity, out var hours) ? WorkingCalendar.AddWorkingHours(now, hours, holidays) : (DateTimeOffset?)null;
            var oldDue = issue.DueDate;
            issue.DueDate = due;
            issue.UpdatedAt = now;
            db.IssueActivities.Add(new IssueActivity
            {
                IssueId = issue.Id,
                UserId = currentUser.UserId,
                Kind = ActivityKind.SprintEvent,
                Body = $"Testing of {sprint.Name} completed.",
                OldValue = oldDue?.ToString("O", CultureInfo.InvariantCulture),
                NewValue = due?.ToString("O", CultureInfo.InvariantCulture),
                CreatedAt = now,
            });
        }
    }

    /// <summary>In-app notification (and the one summary mail, ADR-029) for project members and assigned developers.</summary>
    private async Task NotifyAsync(TestingSprint sprint, List<Issue> issues, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var members = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == sprint.ProjectId && m.User.IsActive)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        var assignees = issues.Where(i => i.AssigneeId is not null).Select(i => i.AssigneeId!.Value);
        var recipients = members.Concat(assignees).Distinct().Where(id => id != currentUser.UserId).ToList();

        var unassigned = issues.Count(i => i.AssigneeId is null);
        var body = issues.Count == 1 ? "1 issue is ready for development" : $"{issues.Count} issues are ready for development";
        body += unassigned > 0 ? $"; {unassigned} still unassigned – pick them from My work." : ".";

        foreach (var userId in recipients)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.TestingCompleted,
                SprintId = sprint.Id,
                Title = $"{sprint.Project.ProjectKey}: testing of {sprint.Name} completed",
                Body = body,
                IsRead = false,
                CreatedAt = now,
            });
        }
    }
}
