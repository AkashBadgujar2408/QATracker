-- 007_SprintCloseAndCarryForward.sql – sprint close and carry forward (S29, ADR-039).
--   SprintEvents.Kind       + IssuesCarriedForward (on the sprint the issues leave) and IssuesCarriedIn (on the
--                             sprint they join), so both sprint histories record the move.
--   SprintSnapshots         + CarriedToSprintId – an issue carried out of a sprint is frozen in that sprint's
--                             snapshot at the moment it leaves, with the sprint it went to; the rest is frozen at
--                             closing. The report of a closed sprint is built from these rows only.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.SprintEvents DROP CONSTRAINT CK_SprintEvents_Kind;
ALTER TABLE dbo.SprintEvents ADD CONSTRAINT CK_SprintEvents_Kind
    CHECK (Kind IN (N'Started', N'TestingCompleted', N'ReopenedForTesting', N'Closed', N'IssuesCarriedForward', N'IssuesCarriedIn'));

ALTER TABLE dbo.SprintSnapshots ADD CarriedToSprintId int NULL
    CONSTRAINT FK_SprintSnapshots_CarriedToSprint REFERENCES dbo.TestingSprints (Id);

COMMIT TRANSACTION;
