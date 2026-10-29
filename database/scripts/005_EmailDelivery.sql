-- 005_EmailDelivery.sql – e-mail notifications (S19, ADR-019 / ADR-029).
--   Notifications.EmailedAt  – set when the notification went into the outbox (instantly or in the daily digest), so
--                              nothing is mailed twice.
--   EmailOutbox.NextAttemptAt – retry back-off of the background sender (NULL = send as soon as possible).
--   JobRuns                  – last run of each scheduled job (daily digest), for catch-up after a restart and the
--                              jobs admin later.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.Notifications ADD EmailedAt datetimeoffset NULL;

ALTER TABLE dbo.EmailOutbox ADD NextAttemptAt datetimeoffset NULL;

CREATE TABLE dbo.JobRuns
(
    Name       nvarchar(100)  NOT NULL CONSTRAINT PK_JobRuns PRIMARY KEY,
    LastRunAt  datetimeoffset NOT NULL,
    LastResult nvarchar(1000) NULL
);

GO

-- Daily digest: comment notifications not yet mailed (new column: separate batch).
CREATE INDEX IX_Notifications_Digest ON dbo.Notifications (Type, CreatedAt) INCLUDE (UserId) WHERE EmailedAt IS NULL;

COMMIT TRANSACTION;
