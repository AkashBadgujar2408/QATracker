using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using QaTracker.Domain.Entities;

namespace QaTracker.Application.Abstractions;

/// <summary>
/// Data access used by handlers. Implemented by the scaffolded QaTrackerDbContext (partial class in Infrastructure);
/// property names must match the scaffolded DbSets.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Attachment> Attachments { get; }
    DbSet<Client> Clients { get; }
    DbSet<DeployNotice> DeployNotices { get; }
    DbSet<Deployment> Deployments { get; }
    DbSet<EmailOutbox> EmailOutboxes { get; }
    DbSet<Holiday> Holidays { get; }
    DbSet<Issue> Issues { get; }
    DbSet<IssueActivity> IssueActivities { get; }
    DbSet<IssueLink> IssueLinks { get; }
    DbSet<IssueWatcher> IssueWatchers { get; }
    DbSet<JobRun> JobRuns { get; }
    DbSet<Mention> Mentions { get; }
    DbSet<Module> Modules { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectEnvironment> ProjectEnvironments { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<SavedView> SavedViews { get; }
    DbSet<SlaPolicy> SlaPolicies { get; }
    DbSet<SprintEvent> SprintEvents { get; }
    DbSet<SprintPhase> SprintPhases { get; }
    DbSet<SprintSnapshot> SprintSnapshots { get; }
    DbSet<TestingSprint> TestingSprints { get; }
    DbSet<User> Users { get; }

    DatabaseFacade Database { get; }

    /// <summary>Change-tracking entry, e.g. to set the original RowVersion for optimistic concurrency.</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
