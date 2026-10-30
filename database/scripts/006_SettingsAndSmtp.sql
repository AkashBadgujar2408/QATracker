-- 006_SettingsAndSmtp.sql – admin settings (S28, ADR-038).
--   SlaPolicies.RowVersion, SprintPhases.RowVersion – the settings pages update them with optimistic concurrency.
--   EmailSettings – SMTP server and sender edited in the app (one row, Id = 1). While the row is missing the
--                   Email section of appsettings / user secrets applies, as before. The password is stored as typed
--                   (like user passwords, ADR-009) and never returned by the API.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.SlaPolicies ADD RowVersion rowversion NOT NULL;

ALTER TABLE dbo.SprintPhases ADD RowVersion rowversion NOT NULL;

CREATE TABLE dbo.EmailSettings
(
    Id            int                NOT NULL CONSTRAINT PK_EmailSettings PRIMARY KEY CONSTRAINT CK_EmailSettings_Single CHECK (Id = 1),
    SmtpHost      nvarchar(255)      NOT NULL,
    SmtpPort      int                NOT NULL CONSTRAINT CK_EmailSettings_SmtpPort CHECK (SmtpPort BETWEEN 1 AND 65535),
    SmtpEnableSsl bit                NOT NULL,
    SmtpUserName  nvarchar(256)      NULL,
    SmtpPassword  nvarchar(256)      NULL,
    FromAddress   nvarchar(256)      NOT NULL,
    FromName      nvarchar(100)      NOT NULL,
    UpdatedAt     datetimeoffset     NOT NULL,
    UpdatedById   int                NOT NULL CONSTRAINT FK_EmailSettings_UpdatedBy REFERENCES dbo.Users (Id),
    RowVersion    rowversion         NOT NULL
);

COMMIT TRANSACTION;
