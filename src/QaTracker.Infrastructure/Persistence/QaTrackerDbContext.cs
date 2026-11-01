using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using QaTracker.Domain.Entities;

namespace QaTracker.Infrastructure.Persistence;

public partial class QaTrackerDbContext : DbContext
{
    public QaTrackerDbContext(DbContextOptions<QaTrackerDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Attachment> Attachments { get; set; }

    public virtual DbSet<Client> Clients { get; set; }

    public virtual DbSet<DeployNotice> DeployNotices { get; set; }

    public virtual DbSet<Deployment> Deployments { get; set; }

    public virtual DbSet<EmailOutbox> EmailOutboxes { get; set; }

    public virtual DbSet<EmailSetting> EmailSettings { get; set; }

    public virtual DbSet<Holiday> Holidays { get; set; }

    public virtual DbSet<Issue> Issues { get; set; }

    public virtual DbSet<IssueActivity> IssueActivities { get; set; }

    public virtual DbSet<IssueLink> IssueLinks { get; set; }

    public virtual DbSet<IssueWatcher> IssueWatchers { get; set; }

    public virtual DbSet<JobRun> JobRuns { get; set; }

    public virtual DbSet<Mention> Mentions { get; set; }

    public virtual DbSet<Module> Modules { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<NotificationPreference> NotificationPreferences { get; set; }

    public virtual DbSet<Project> Projects { get; set; }

    public virtual DbSet<ProjectEnvironment> ProjectEnvironments { get; set; }

    public virtual DbSet<ProjectMember> ProjectMembers { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<SavedView> SavedViews { get; set; }

    public virtual DbSet<SlaPolicy> SlaPolicies { get; set; }

    public virtual DbSet<SprintEvent> SprintEvents { get; set; }

    public virtual DbSet<SprintPhase> SprintPhases { get; set; }

    public virtual DbSet<SprintSnapshot> SprintSnapshots { get; set; }

    public virtual DbSet<TestingSprint> TestingSprints { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasIndex(e => e.IssueId, "IX_Attachments_Issue");

            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.StoragePath).HasMaxLength(500);
            entity.Property(e => e.ThumbnailPath).HasMaxLength(500);

            entity.HasOne(d => d.Activity).WithMany(p => p.Attachments)
                .HasForeignKey(d => d.ActivityId)
                .HasConstraintName("FK_Attachments_Activity");

            entity.HasOne(d => d.Issue).WithMany(p => p.Attachments)
                .HasForeignKey(d => d.IssueId)
                .HasConstraintName("FK_Attachments_Issue");

            entity.HasOne(d => d.UploadedBy).WithMany(p => p.Attachments)
                .HasForeignKey(d => d.UploadedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Attachments_UploadedBy");
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasIndex(e => e.Name, "UX_Clients_Name").IsUnique();

            entity.Property(e => e.ContactPerson).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.ClientCreatedBies)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Clients_CreatedBy");

            entity.HasOne(d => d.UpdatedBy).WithMany(p => p.ClientUpdatedBies)
                .HasForeignKey(d => d.UpdatedById)
                .HasConstraintName("FK_Clients_UpdatedBy");
        });

        modelBuilder.Entity<DeployNotice>(entity =>
        {
            entity.Property(e => e.Note).HasMaxLength(2000);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.DeployNotices)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DeployNotices_CreatedBy");

            entity.HasOne(d => d.Environment).WithMany(p => p.DeployNotices)
                .HasForeignKey(d => d.EnvironmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DeployNotices_Environment");

            entity.HasOne(d => d.Project).WithMany(p => p.DeployNotices)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_DeployNotices_Project");
        });

        modelBuilder.Entity<Deployment>(entity =>
        {
            entity.HasIndex(e => e.IssueId, "IX_Deployments_Issue");

            entity.HasIndex(e => new { e.RetestResult, e.EnvironmentId }, "IX_Deployments_RetestResult_Environment");

            entity.Property(e => e.Note).HasMaxLength(2000);
            entity.Property(e => e.RetestComment).HasMaxLength(2000);
            entity.Property(e => e.RetestResult).HasMaxLength(10);
            entity.Property(e => e.Version).HasMaxLength(100);

            entity.HasOne(d => d.DeployNotice).WithMany(p => p.Deployments)
                .HasForeignKey(d => d.DeployNoticeId)
                .HasConstraintName("FK_Deployments_DeployNotice");

            entity.HasOne(d => d.DeployedBy).WithMany(p => p.DeploymentDeployedBies)
                .HasForeignKey(d => d.DeployedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Deployments_DeployedBy");

            entity.HasOne(d => d.Environment).WithMany(p => p.Deployments)
                .HasForeignKey(d => d.EnvironmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Deployments_Environment");

            entity.HasOne(d => d.Issue).WithMany(p => p.Deployments)
                .HasForeignKey(d => d.IssueId)
                .HasConstraintName("FK_Deployments_Issue");

            entity.HasOne(d => d.RetestedBy).WithMany(p => p.DeploymentRetestedBies)
                .HasForeignKey(d => d.RetestedById)
                .HasConstraintName("FK_Deployments_RetestedBy");
        });

        modelBuilder.Entity<EmailOutbox>(entity =>
        {
            entity.ToTable("EmailOutbox");

            entity.HasIndex(e => e.CreatedAt, "IX_EmailOutbox_Pending").HasFilter("([SentAt] IS NULL)");

            entity.Property(e => e.LastError).HasMaxLength(2000);
            entity.Property(e => e.Subject).HasMaxLength(300);
            entity.Property(e => e.ToAddress).HasMaxLength(256);
        });

        modelBuilder.Entity<EmailSetting>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.FromAddress).HasMaxLength(256);
            entity.Property(e => e.FromName).HasMaxLength(100);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.SmtpHost).HasMaxLength(255);
            entity.Property(e => e.SmtpPassword).HasMaxLength(256);
            entity.Property(e => e.SmtpUserName).HasMaxLength(256);

            entity.HasOne(d => d.UpdatedBy).WithMany(p => p.EmailSettings)
                .HasForeignKey(d => d.UpdatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_EmailSettings_UpdatedBy");
        });

        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.HasKey(e => e.Date);

            entity.Property(e => e.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Issue>(entity =>
        {
            entity.HasIndex(e => new { e.AssigneeId, e.Status }, "IX_Issues_Assignee_Status");

            entity.HasIndex(e => e.ClosedAt, "IX_Issues_ClosedAt").HasFilter("([ClosedAt] IS NOT NULL)");

            entity.HasIndex(e => e.CreatedAt, "IX_Issues_CreatedAt");

            entity.HasIndex(e => e.DuplicateOfIssueId, "IX_Issues_DuplicateOfIssue").HasFilter("([DuplicateOfIssueId] IS NOT NULL)");

            entity.HasIndex(e => new { e.ModuleId, e.ScreenName }, "IX_Issues_Module_ScreenName");

            entity.HasIndex(e => new { e.ProjectId, e.CreatedAt }, "IX_Issues_Project_CreatedAt");

            entity.HasIndex(e => new { e.ProjectId, e.Severity, e.Status }, "IX_Issues_Project_Severity_Status");

            entity.HasIndex(e => new { e.ProjectId, e.Status, e.DueDate }, "IX_Issues_Project_Status_DueDate");

            entity.HasIndex(e => new { e.SprintId, e.Status }, "IX_Issues_Sprint_Status");

            entity.HasIndex(e => e.IssueKey, "UX_Issues_IssueKey").IsUnique();

            entity.HasIndex(e => new { e.ProjectId, e.Number }, "UX_Issues_Project_Number").IsUnique();

            entity.Property(e => e.HoldReason).HasMaxLength(1000);
            entity.Property(e => e.IssueKey).HasMaxLength(20);
            entity.Property(e => e.PageUrl).HasMaxLength(1000);
            entity.Property(e => e.Priority).HasMaxLength(10);
            entity.Property(e => e.RejectReason).HasMaxLength(1000);
            entity.Property(e => e.RejectType).HasMaxLength(20);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.ScreenName).HasMaxLength(150);
            entity.Property(e => e.Severity).HasMaxLength(20);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Type).HasMaxLength(20);

            entity.HasOne(d => d.Assignee).WithMany(p => p.IssueAssignees)
                .HasForeignKey(d => d.AssigneeId)
                .HasConstraintName("FK_Issues_Assignee");

            entity.HasOne(d => d.CarriedFromSprint).WithMany(p => p.IssueCarriedFromSprints)
                .HasForeignKey(d => d.CarriedFromSprintId)
                .HasConstraintName("FK_Issues_CarriedFromSprint");

            entity.HasOne(d => d.DuplicateOfIssue).WithMany(p => p.InverseDuplicateOfIssue)
                .HasForeignKey(d => d.DuplicateOfIssueId)
                .HasConstraintName("FK_Issues_DuplicateOfIssue");

            entity.HasOne(d => d.FoundOnEnvironment).WithMany(p => p.Issues)
                .HasForeignKey(d => d.FoundOnEnvironmentId)
                .HasConstraintName("FK_Issues_FoundOnEnvironment");

            entity.HasOne(d => d.Module).WithMany(p => p.Issues)
                .HasForeignKey(d => d.ModuleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Issues_Module");

            entity.HasOne(d => d.Project).WithMany(p => p.Issues)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Issues_Project");

            entity.HasOne(d => d.Reporter).WithMany(p => p.IssueReporters)
                .HasForeignKey(d => d.ReporterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Issues_Reporter");

            entity.HasOne(d => d.Sprint).WithMany(p => p.IssueSprints)
                .HasForeignKey(d => d.SprintId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Issues_Sprint");
        });

        modelBuilder.Entity<IssueActivity>(entity =>
        {
            entity.HasIndex(e => new { e.IssueId, e.CreatedAt }, "IX_IssueActivities_Issue_CreatedAt");

            entity.HasIndex(e => new { e.Kind, e.NewValue }, "IX_IssueActivities_Kind_NewValue");

            entity.Property(e => e.Kind).HasMaxLength(20);
            entity.Property(e => e.NewValue).HasMaxLength(500);
            entity.Property(e => e.OldValue).HasMaxLength(500);

            entity.HasOne(d => d.Deployment).WithMany(p => p.IssueActivities)
                .HasForeignKey(d => d.DeploymentId)
                .HasConstraintName("FK_IssueActivities_Deployment");

            entity.HasOne(d => d.Environment).WithMany(p => p.IssueActivities)
                .HasForeignKey(d => d.EnvironmentId)
                .HasConstraintName("FK_IssueActivities_Environment");

            entity.HasOne(d => d.Issue).WithMany(p => p.IssueActivities)
                .HasForeignKey(d => d.IssueId)
                .HasConstraintName("FK_IssueActivities_Issue");

            entity.HasOne(d => d.User).WithMany(p => p.IssueActivities)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_IssueActivities_User");
        });

        modelBuilder.Entity<IssueLink>(entity =>
        {
            entity.HasKey(e => new { e.FromIssueId, e.ToIssueId });

            entity.HasIndex(e => e.ToIssueId, "IX_IssueLinks_ToIssue");

            entity.Property(e => e.Type).HasMaxLength(20);

            entity.HasOne(d => d.FromIssue).WithMany(p => p.IssueLinkFromIssues)
                .HasForeignKey(d => d.FromIssueId)
                .HasConstraintName("FK_IssueLinks_FromIssue");

            entity.HasOne(d => d.ToIssue).WithMany(p => p.IssueLinkToIssues)
                .HasForeignKey(d => d.ToIssueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_IssueLinks_ToIssue");
        });

        modelBuilder.Entity<IssueWatcher>(entity =>
        {
            entity.HasKey(e => new { e.IssueId, e.UserId });

            entity.HasOne(d => d.Issue).WithMany(p => p.IssueWatchers)
                .HasForeignKey(d => d.IssueId)
                .HasConstraintName("FK_IssueWatchers_Issue");

            entity.HasOne(d => d.User).WithMany(p => p.IssueWatchers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_IssueWatchers_User");
        });

        modelBuilder.Entity<JobRun>(entity =>
        {
            entity.HasKey(e => e.Name);

            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.LastResult).HasMaxLength(1000);
        });

        modelBuilder.Entity<Mention>(entity =>
        {
            entity.HasIndex(e => new { e.ActivityId, e.UserId }, "UX_Mentions_Activity_User").IsUnique();

            entity.HasOne(d => d.Activity).WithMany(p => p.Mentions)
                .HasForeignKey(d => d.ActivityId)
                .HasConstraintName("FK_Mentions_Activity");

            entity.HasOne(d => d.User).WithMany(p => p.Mentions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Mentions_User");
        });

        modelBuilder.Entity<Module>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.Name }, "UX_Modules_Project_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.ModuleCreatedBies)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Modules_CreatedBy");

            entity.HasOne(d => d.Project).WithMany(p => p.Modules)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Modules_Project");

            entity.HasOne(d => d.UpdatedBy).WithMany(p => p.ModuleUpdatedBies)
                .HasForeignKey(d => d.UpdatedById)
                .HasConstraintName("FK_Modules_UpdatedBy");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasIndex(e => new { e.Type, e.CreatedAt }, "IX_Notifications_Digest").HasFilter("([EmailedAt] IS NULL)");

            entity.HasIndex(e => e.IssueId, "IX_Notifications_Issue").HasFilter("([IssueId] IS NOT NULL)");

            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt }, "IX_Notifications_User_IsRead_CreatedAt");

            entity.Property(e => e.Body).HasMaxLength(1000);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Type).HasMaxLength(30);

            entity.HasOne(d => d.Issue).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.IssueId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Notifications_Issue");

            entity.HasOne(d => d.Sprint).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.SprintId)
                .HasConstraintName("FK_Notifications_Sprint");

            entity.HasOne(d => d.User).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Notifications_User");
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.Type });

            entity.Property(e => e.Type).HasMaxLength(30);

            entity.HasOne(d => d.User).WithMany(p => p.NotificationPreferences)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NotificationPreferences_User");
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasIndex(e => new { e.ClientId, e.Name }, "UX_Projects_Client_Name").IsUnique();

            entity.HasIndex(e => e.ProjectKey, "UX_Projects_ProjectKey").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.ProjectKey).HasMaxLength(8);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(d => d.Client).WithMany(p => p.Projects)
                .HasForeignKey(d => d.ClientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Projects_Client");

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.ProjectCreatedBies)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Projects_CreatedBy");

            entity.HasOne(d => d.UpdatedBy).WithMany(p => p.ProjectUpdatedBies)
                .HasForeignKey(d => d.UpdatedById)
                .HasConstraintName("FK_Projects_UpdatedBy");
        });

        modelBuilder.Entity<ProjectEnvironment>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.Name }, "UX_ProjectEnvironments_Project_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Type).HasMaxLength(10);
            entity.Property(e => e.Url).HasMaxLength(500);

            entity.HasOne(d => d.CreatedBy).WithMany(p => p.ProjectEnvironmentCreatedBies)
                .HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProjectEnvironments_CreatedBy");

            entity.HasOne(d => d.Project).WithMany(p => p.ProjectEnvironments)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProjectEnvironments_Project");

            entity.HasOne(d => d.UpdatedBy).WithMany(p => p.ProjectEnvironmentUpdatedBies)
                .HasForeignKey(d => d.UpdatedById)
                .HasConstraintName("FK_ProjectEnvironments_UpdatedBy");
        });

        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.HasKey(e => new { e.ProjectId, e.UserId });

            entity.HasOne(d => d.Project).WithMany(p => p.ProjectMembers)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProjectMembers_Project");

            entity.HasOne(d => d.User).WithMany(p => p.ProjectMembers)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProjectMembers_User");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_RefreshTokens_User");

            entity.HasIndex(e => e.TokenHash, "UX_RefreshTokens_TokenHash").IsUnique();

            entity.Property(e => e.CreatedByIp).HasMaxLength(45);
            entity.Property(e => e.TokenHash).HasMaxLength(64);

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RefreshTokens_User");
        });

        modelBuilder.Entity<SavedView>(entity =>
        {
            entity.HasIndex(e => new { e.UserId, e.Page, e.Name }, "UX_SavedViews_User_Page_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Page).HasMaxLength(50);

            entity.HasOne(d => d.User).WithMany(p => p.SavedViews)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SavedViews_User");
        });

        modelBuilder.Entity<SlaPolicy>(entity =>
        {
            entity.HasIndex(e => e.Severity, "UX_SlaPolicies_Severity").IsUnique();

            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Severity).HasMaxLength(20);
        });

        modelBuilder.Entity<SprintEvent>(entity =>
        {
            entity.HasIndex(e => new { e.SprintId, e.At }, "IX_SprintEvents_Sprint_At");

            entity.Property(e => e.Kind).HasMaxLength(30);
            entity.Property(e => e.Note).HasMaxLength(1000);

            entity.HasOne(d => d.Sprint).WithMany(p => p.SprintEvents)
                .HasForeignKey(d => d.SprintId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SprintEvents_Sprint");

            entity.HasOne(d => d.User).WithMany(p => p.SprintEvents)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SprintEvents_User");
        });

        modelBuilder.Entity<SprintPhase>(entity =>
        {
            entity.HasIndex(e => e.Name, "UX_SprintPhases_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<SprintSnapshot>(entity =>
        {
            entity.HasKey(e => new { e.SprintId, e.IssueId });

            entity.HasIndex(e => e.IssueId, "IX_SprintSnapshots_Issue");

            entity.Property(e => e.SeverityAtClose).HasMaxLength(20);
            entity.Property(e => e.StatusAtClose).HasMaxLength(20);

            entity.HasOne(d => d.AssigneeAtClose).WithMany(p => p.SprintSnapshots)
                .HasForeignKey(d => d.AssigneeAtCloseId)
                .HasConstraintName("FK_SprintSnapshots_AssigneeAtClose");

            entity.HasOne(d => d.CarriedToSprint).WithMany(p => p.SprintSnapshotCarriedToSprints)
                .HasForeignKey(d => d.CarriedToSprintId)
                .HasConstraintName("FK_SprintSnapshots_CarriedToSprint");

            entity.HasOne(d => d.Issue).WithMany(p => p.SprintSnapshots)
                .HasForeignKey(d => d.IssueId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SprintSnapshots_Issue");

            entity.HasOne(d => d.Sprint).WithMany(p => p.SprintSnapshotSprints)
                .HasForeignKey(d => d.SprintId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_SprintSnapshots_Sprint");
        });

        modelBuilder.Entity<TestingSprint>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.Status }, "IX_TestingSprints_Project_Status");

            entity.HasIndex(e => e.ProjectId, "UX_TestingSprints_OneTestingPerProject")
                .IsUnique()
                .HasFilter("([Status]=N'Testing')");

            entity.HasIndex(e => new { e.ProjectId, e.Name }, "UX_TestingSprints_Project_Name").IsUnique();

            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.Status).HasMaxLength(20);

            entity.HasOne(d => d.ClosedBy).WithMany(p => p.TestingSprintClosedBies)
                .HasForeignKey(d => d.ClosedById)
                .HasConstraintName("FK_TestingSprints_ClosedBy");

            entity.HasOne(d => d.DefaultEnvironment).WithMany(p => p.TestingSprints)
                .HasForeignKey(d => d.DefaultEnvironmentId)
                .HasConstraintName("FK_TestingSprints_DefaultEnvironment");

            entity.HasOne(d => d.Phase).WithMany(p => p.TestingSprints)
                .HasForeignKey(d => d.PhaseId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TestingSprints_Phase");

            entity.HasOne(d => d.Project).WithOne(p => p.TestingSprint)
                .HasForeignKey<TestingSprint>(d => d.ProjectId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TestingSprints_Project");

            entity.HasOne(d => d.StartedBy).WithMany(p => p.TestingSprintStartedBies)
                .HasForeignKey(d => d.StartedById)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TestingSprints_StartedBy");

            entity.HasOne(d => d.TestingCompletedBy).WithMany(p => p.TestingSprintTestingCompletedBies)
                .HasForeignKey(d => d.TestingCompletedById)
                .HasConstraintName("FK_TestingSprints_TestingCompletedBy");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.Email, "UX_Users_Email").IsUnique();

            entity.HasIndex(e => e.UserName, "UX_Users_UserName").IsUnique();

            entity.Property(e => e.AvatarColor).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Password).HasMaxLength(100);
            entity.Property(e => e.Role).HasMaxLength(20);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
            entity.Property(e => e.SecurityStamp).HasMaxLength(64);
            entity.Property(e => e.UserName).HasMaxLength(50);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
