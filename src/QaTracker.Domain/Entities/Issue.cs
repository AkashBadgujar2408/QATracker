using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class Issue
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public int Number { get; set; }

    public string IssueKey { get; set; } = null!;

    public int SprintId { get; set; }

    public int? CarriedFromSprintId { get; set; }

    public int ModuleId { get; set; }

    public string? ScreenName { get; set; }

    public int? FoundOnEnvironmentId { get; set; }

    public string? PageUrl { get; set; }

    public IssueType Type { get; set; }

    public string Title { get; set; } = null!;

    public string? Steps { get; set; }

    public string? ExpectedResult { get; set; }

    public string ActualResult { get; set; } = null!;

    public string? Impact { get; set; }

    public Severity Severity { get; set; }

    public Priority Priority { get; set; }

    public IssueStatus Status { get; set; }

    public string? HoldReason { get; set; }

    public RejectType? RejectType { get; set; }

    public string? RejectReason { get; set; }

    public int? DuplicateOfIssueId { get; set; }

    public int ReporterId { get; set; }

    public int? AssigneeId { get; set; }

    public DateTimeOffset? DueDate { get; set; }

    public int ReopenCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? FirstFixedAt { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual User? Assignee { get; set; }

    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    public virtual TestingSprint? CarriedFromSprint { get; set; }

    public virtual ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();

    public virtual Issue? DuplicateOfIssue { get; set; }

    public virtual ProjectEnvironment? FoundOnEnvironment { get; set; }

    public virtual ICollection<Issue> InverseDuplicateOfIssue { get; set; } = new List<Issue>();

    public virtual ICollection<IssueActivity> IssueActivities { get; set; } = new List<IssueActivity>();

    public virtual ICollection<IssueLink> IssueLinkFromIssues { get; set; } = new List<IssueLink>();

    public virtual ICollection<IssueLink> IssueLinkToIssues { get; set; } = new List<IssueLink>();

    public virtual ICollection<IssueWatcher> IssueWatchers { get; set; } = new List<IssueWatcher>();

    public virtual Module Module { get; set; } = null!;

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual Project Project { get; set; } = null!;

    public virtual User Reporter { get; set; } = null!;

    public virtual TestingSprint Sprint { get; set; } = null!;

    public virtual ICollection<SprintSnapshot> SprintSnapshots { get; set; } = new List<SprintSnapshot>();
}
