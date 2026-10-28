-- 004_IssueDeleteIndexes.sql – indexes for the foreign keys checked when an issue is deleted (S10).
-- Without them every issue delete scans IssueLinks, Notifications, Issues and SprintSnapshots, which blocks and can
-- deadlock with concurrent inserts (seen once in the parallel integration tests).

SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE INDEX IX_IssueLinks_ToIssue ON dbo.IssueLinks (ToIssueId);
CREATE INDEX IX_Notifications_Issue ON dbo.Notifications (IssueId) WHERE IssueId IS NOT NULL;
CREATE INDEX IX_Issues_DuplicateOfIssue ON dbo.Issues (DuplicateOfIssueId) WHERE DuplicateOfIssueId IS NOT NULL;
CREATE INDEX IX_SprintSnapshots_Issue ON dbo.SprintSnapshots (IssueId);

COMMIT TRANSACTION;
