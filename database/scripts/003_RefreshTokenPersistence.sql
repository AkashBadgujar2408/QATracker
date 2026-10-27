-- 003_RefreshTokenPersistence.sql – "Remember me" (S05): persistent cookie vs browser-session cookie.
-- The flag is carried over when the token rotates. (Temporary default only to fill existing rows – ADR-010: no defaults.)

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.RefreshTokens ADD IsPersistent bit NOT NULL CONSTRAINT DF_RefreshTokens_IsPersistent_Tmp DEFAULT (1);
ALTER TABLE dbo.RefreshTokens DROP CONSTRAINT DF_RefreshTokens_IsPersistent_Tmp;

COMMIT TRANSACTION;
