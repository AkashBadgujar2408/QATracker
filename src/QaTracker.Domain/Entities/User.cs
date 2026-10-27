using System;
using System.Collections.Generic;
using QaTracker.Domain.Enums;

namespace QaTracker.Domain.Entities;

public partial class User
{
    public int Id { get; set; }

    public string UserName { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string SecurityStamp { get; set; } = null!;

    public UserRole Role { get; set; }

    public bool IsActive { get; set; }

    public bool MustChangePassword { get; set; }

    public string? AvatarColor { get; set; }

    public int AccessFailedCount { get; set; }

    public DateTimeOffset? LockoutEndAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();

    public virtual ICollection<Client> ClientCreatedBies { get; set; } = new List<Client>();

    public virtual ICollection<Client> ClientUpdatedBies { get; set; } = new List<Client>();

    public virtual ICollection<DeployNotice> DeployNotices { get; set; } = new List<DeployNotice>();

    public virtual ICollection<Deployment> DeploymentDeployedBies { get; set; } = new List<Deployment>();

    public virtual ICollection<Deployment> DeploymentRetestedBies { get; set; } = new List<Deployment>();

    public virtual ICollection<IssueActivity> IssueActivities { get; set; } = new List<IssueActivity>();

    public virtual ICollection<Issue> IssueAssignees { get; set; } = new List<Issue>();

    public virtual ICollection<Issue> IssueReporters { get; set; } = new List<Issue>();

    public virtual ICollection<IssueWatcher> IssueWatchers { get; set; } = new List<IssueWatcher>();

    public virtual ICollection<Mention> Mentions { get; set; } = new List<Mention>();

    public virtual ICollection<Module> ModuleCreatedBies { get; set; } = new List<Module>();

    public virtual ICollection<Module> ModuleUpdatedBies { get; set; } = new List<Module>();

    public virtual ICollection<NotificationPreference> NotificationPreferences { get; set; } = new List<NotificationPreference>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<Project> ProjectCreatedBies { get; set; } = new List<Project>();

    public virtual ICollection<ProjectEnvironment> ProjectEnvironmentCreatedBies { get; set; } = new List<ProjectEnvironment>();

    public virtual ICollection<ProjectEnvironment> ProjectEnvironmentUpdatedBies { get; set; } = new List<ProjectEnvironment>();

    public virtual ICollection<ProjectMember> ProjectMembers { get; set; } = new List<ProjectMember>();

    public virtual ICollection<Project> ProjectUpdatedBies { get; set; } = new List<Project>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual ICollection<SavedView> SavedViews { get; set; } = new List<SavedView>();

    public virtual ICollection<SprintEvent> SprintEvents { get; set; } = new List<SprintEvent>();

    public virtual ICollection<SprintSnapshot> SprintSnapshots { get; set; } = new List<SprintSnapshot>();

    public virtual ICollection<TestingSprint> TestingSprintClosedBies { get; set; } = new List<TestingSprint>();

    public virtual ICollection<TestingSprint> TestingSprintStartedBies { get; set; } = new List<TestingSprint>();

    public virtual ICollection<TestingSprint> TestingSprintTestingCompletedBies { get; set; } = new List<TestingSprint>();
}
