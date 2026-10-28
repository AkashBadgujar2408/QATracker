using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using QaTracker.Application.Abstractions;
using QaTracker.Application.Common;
using QaTracker.Domain.Entities;
using QaTracker.Domain.Enums;
using Severity = QaTracker.Domain.Enums.Severity;

namespace QaTracker.Application.Features.Issues;

/// <summary>Full issue (plan 7.1). Rich-text fields are sanitised HTML.</summary>
public sealed record IssueDto(
    int Id,
    string Key,
    int Number,
    int ClientId,
    string ClientName,
    int ProjectId,
    string ProjectKey,
    string ProjectName,
    int SprintId,
    string SprintName,
    SprintStatus SprintStatus,
    int ModuleId,
    string ModuleName,
    string? ScreenName,
    int? FoundOnEnvironmentId,
    string? FoundOnEnvironmentName,
    string? PageUrl,
    IssueType Type,
    string Title,
    string? Steps,
    string? ExpectedResult,
    string ActualResult,
    string? Impact,
    Severity Severity,
    Priority Priority,
    IssueStatus Status,
    int ReporterId,
    string ReporterName,
    int? AssigneeId,
    string? AssigneeName,
    DateTimeOffset? DueDate,
    int ReopenCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string RowVersion)
{
    /// <summary>What the current user may do with the issue's details (workflow actions come with IssueWorkflow).</summary>
    public IssuePermissions Permissions { get; init; } = IssuePermissions.None;

    public static readonly Expression<Func<Issue, IssueDto>> Projection = i => new IssueDto(
        i.Id,
        i.IssueKey,
        i.Number,
        i.Project.ClientId,
        i.Project.Client.Name,
        i.ProjectId,
        i.Project.ProjectKey,
        i.Project.Name,
        i.SprintId,
        i.Sprint.Name,
        i.Sprint.Status,
        i.ModuleId,
        i.Module.Name,
        i.ScreenName,
        i.FoundOnEnvironmentId,
        i.FoundOnEnvironment != null ? i.FoundOnEnvironment.Name : null,
        i.PageUrl,
        i.Type,
        i.Title,
        i.Steps,
        i.ExpectedResult,
        i.ActualResult,
        i.Impact,
        i.Severity,
        i.Priority,
        i.Status,
        i.ReporterId,
        i.Reporter.FullName,
        i.AssigneeId,
        i.Assignee != null ? i.Assignee.FullName : null,
        i.DueDate,
        i.ReopenCount,
        i.CreatedAt,
        i.UpdatedAt,
        Convert.ToBase64String(i.RowVersion));
}

public sealed record IssuePermissions(bool CanEdit, bool CanDelete)
{
    public static readonly IssuePermissions None = new(false, false);
}

/// <summary>A row of the issue list.</summary>
public sealed record IssueListItemDto(
    int Id,
    string Key,
    string Title,
    int ProjectId,
    string ProjectKey,
    int SprintId,
    string SprintName,
    string ModuleName,
    string? ScreenName,
    IssueType Type,
    Severity Severity,
    Priority Priority,
    IssueStatus Status,
    int? AssigneeId,
    string? AssigneeName,
    string ReporterName,
    DateTimeOffset? DueDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static readonly Expression<Func<Issue, IssueListItemDto>> Projection = i => new IssueListItemDto(
        i.Id,
        i.IssueKey,
        i.Title,
        i.ProjectId,
        i.Project.ProjectKey,
        i.SprintId,
        i.Sprint.Name,
        i.Module.Name,
        i.ScreenName,
        i.Type,
        i.Severity,
        i.Priority,
        i.Status,
        i.AssigneeId,
        i.Assignee != null ? i.Assignee.FullName : null,
        i.Reporter.FullName,
        i.DueDate,
        i.CreatedAt,
        i.UpdatedAt);
}

/// <summary>Editable issue fields, shared by create and update. Type, severity and priority are required (no defaults – ADR-016).</summary>
public interface IIssueFields
{
    int ModuleId { get; }
    string? ScreenName { get; }
    int? FoundOnEnvironmentId { get; }
    string? PageUrl { get; }
    IssueType? Type { get; }
    string Title { get; }
    string? Steps { get; }
    string? ExpectedResult { get; }
    string ActualResult { get; }
    string? Impact { get; }
    Severity? Severity { get; }
    Priority? Priority { get; }
    int? AssigneeId { get; }
}

internal static class IssueRules
{
    public const int RichTextMaxLength = 20_000;

    public static void Apply<T>(AbstractValidator<T> validator)
        where T : IIssueFields
    {
        validator.RuleFor(x => x.ModuleId).GreaterThan(0).WithMessage("Module is required.");
        validator.RuleFor(x => x.ScreenName).MaximumLength(150);
        validator.RuleFor(x => x.PageUrl).MaximumLength(1000)
            .Must(url => Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .When(x => !string.IsNullOrWhiteSpace(x.PageUrl))
            .WithMessage("Page URL must be a full http(s) address.");
        validator.RuleFor(x => x.Type).NotNull().IsInEnum();
        validator.RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        validator.RuleFor(x => x.Steps).MaximumLength(RichTextMaxLength);
        validator.RuleFor(x => x.ExpectedResult).MaximumLength(RichTextMaxLength);
        validator.RuleFor(x => x.ActualResult).NotEmpty().MaximumLength(RichTextMaxLength);
        validator.RuleFor(x => x.Impact).MaximumLength(RichTextMaxLength);
        validator.RuleFor(x => x.Severity).NotNull().IsInEnum();
        validator.RuleFor(x => x.Priority).NotNull().IsInEnum();
    }

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Module and environment must belong to the project and be active (an unchanged inactive one may stay);
    /// the assignee must be an active developer or admin.
    /// </summary>
    public static async Task<Error?> CheckReferencesAsync(
        IApplicationDbContext db,
        int projectId,
        IIssueFields fields,
        Issue? current,
        CancellationToken cancellationToken)
    {
        if (fields.ModuleId != current?.ModuleId
            && !await db.Modules.AnyAsync(m => m.Id == fields.ModuleId && m.ProjectId == projectId && m.IsActive, cancellationToken))
        {
            return Error.Validation(nameof(fields.ModuleId), "Choose an active module of this project.");
        }

        if (fields.FoundOnEnvironmentId is { } environmentId && environmentId != current?.FoundOnEnvironmentId
            && !await db.ProjectEnvironments.AnyAsync(e => e.Id == environmentId && e.ProjectId == projectId && e.IsActive, cancellationToken))
        {
            return Error.Validation(nameof(fields.FoundOnEnvironmentId), "Choose an active environment of this project.");
        }

        if (fields.AssigneeId is { } assigneeId && assigneeId != current?.AssigneeId
            && !await db.Users.AnyAsync(u => u.Id == assigneeId && u.IsActive && u.Role != UserRole.Tester, cancellationToken))
        {
            return Error.Validation(nameof(fields.AssigneeId), "Assign the issue to an active developer.");
        }

        return null;
    }

    /// <summary>Upper-case key ("pgerp-142" → "PGERP-142").</summary>
    public static string NormalizeKey(string key) => key.Trim().ToUpperInvariant();
}
