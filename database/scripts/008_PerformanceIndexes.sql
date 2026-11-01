-- 008_PerformanceIndexes.sql – indexes from the S31 query review (ADR-041).
--   Found with sys.dm_db_missing_index_* while the analytics (5 000 issues), issue-list (3 000 issues), report,
--   board and work-queue tests ran; only the shapes the dashboards and the issue list use on every load are added.
--   The app writes little (≈ 10 users), so a few wider indexes on Issues cost less than the scans they replace.
--
--   Issues (ProjectId, Status, DueDate)      replaces IX_Issues_Project_Status: open/overdue counts per project,
--                                            list filtered by project + status, due-date sort.
--   Issues (ProjectId, Severity, Status)     open-by-severity, list filtered by severity.
--   Issues (ProjectId, CreatedAt)            created per day (overview, quality) – ClosedAt included for closed per day.
--   Issues (CreatedAt) / (ClosedAt)          the same charts across all projects (overview without a filter).
--   IssueActivities (Kind, NewValue)         status history ("reached Fixed / Reopened") in analytics.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DROP INDEX IX_Issues_Project_Status ON dbo.Issues;
CREATE INDEX IX_Issues_Project_Status_DueDate ON dbo.Issues (ProjectId, Status, DueDate)
    INCLUDE (Severity, AssigneeId, SprintId);

CREATE INDEX IX_Issues_Project_Severity_Status ON dbo.Issues (ProjectId, Severity, Status);

CREATE INDEX IX_Issues_Project_CreatedAt ON dbo.Issues (ProjectId, CreatedAt)
    INCLUDE (Severity, ClosedAt);

CREATE INDEX IX_Issues_CreatedAt ON dbo.Issues (CreatedAt)
    INCLUDE (ProjectId, Severity, ClosedAt);

CREATE INDEX IX_Issues_ClosedAt ON dbo.Issues (ClosedAt)
    INCLUDE (ProjectId, CreatedAt)
    WHERE ClosedAt IS NOT NULL;

CREATE INDEX IX_IssueActivities_Kind_NewValue ON dbo.IssueActivities (Kind, NewValue)
    INCLUDE (IssueId, UserId, CreatedAt);

COMMIT TRANSACTION;
