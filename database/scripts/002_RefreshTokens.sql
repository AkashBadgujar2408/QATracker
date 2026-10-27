-- 002_RefreshTokens.sql – refresh tokens for the httpOnly-cookie session (S04).
-- Only a SHA-256 hash of each token is stored; tokens rotate on every refresh.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.RefreshTokens
(
    Id          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY,
    UserId      int                NOT NULL CONSTRAINT FK_RefreshTokens_User REFERENCES dbo.Users (Id),
    TokenHash   nvarchar(64)       NOT NULL,
    CreatedAt   datetimeoffset     NOT NULL,
    ExpiresAt   datetimeoffset     NOT NULL,
    RevokedAt   datetimeoffset     NULL,
    CreatedByIp nvarchar(45)       NULL,
    CONSTRAINT UX_RefreshTokens_TokenHash UNIQUE (TokenHash)
);

CREATE INDEX IX_RefreshTokens_User ON dbo.RefreshTokens (UserId);

COMMIT TRANSACTION;
