-- 001_InitialSchema.sql – QA Tracker initial schema (plan section 12, database-first – ADR-001).
-- Conventions:
--   * All timestamps are datetimeoffset stored in UTC; the application (IClock) sets every value.
--   * No DEFAULT constraints: EF Core treats CLR defaults (false, 0) as "use DB default", which silently breaks
--     inserts of false/0. The application always provides values.
--   * Enum columns are nvarchar holding the C# enum NAME, guarded by a CHECK constraint
--     (keep in sync with QaTracker.Domain.Enums and CodeTemplates/EFCore/EntityType.t4).
--   * Masters are never deleted (IsActive = 0). Issues are deleted only while their sprint is in Testing,
--     so issue children cascade.
-- Run against the QaTracker database (database/apply-scripts.ps1 does this and records the script).

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-------------------------------------------------------------------------------
-- Users (custom authentication, plain-text passwords – see docs/decisions.md ADR-009)
-------------------------------------------------------------------------------
CREATE TABLE dbo.Users
(
    Id                 int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    -- Login name (ADR-011): 3–50 characters, letters/digits/. _ - only, unique case-insensitively.
    UserName           nvarchar(50)       NOT NULL,
    FullName           nvarchar(100)      NOT NULL,
    -- For notifications only (not used to log in).
    Email              nvarchar(256)      NOT NULL,
    -- Plain text by decision of the product owner (ADR-009) – readable by anyone with database access.
    Password           nvarchar(100)      NOT NULL,
    SecurityStamp      nvarchar(64)       NOT NULL,
    Role               nvarchar(20)       NOT NULL CONSTRAINT CK_Users_Role CHECK (Role IN (N'Tester', N'Developer', N'Admin')),
    IsActive           bit                NOT NULL,
    MustChangePassword bit                NOT NULL,
    AvatarColor        nvarchar(20)       NULL,
    AccessFailedCount  int                NOT NULL,
    LockoutEndAt       datetimeoffset     NULL,
    LastLoginAt        datetimeoffset     NULL,
    CreatedAt          datetimeoffset     NOT NULL,
    UpdatedAt          datetimeoffset     NULL,
    RowVersion         rowversion         NOT NULL,
    CONSTRAINT UX_Users_UserName UNIQUE (UserName),
    CONSTRAINT UX_Users_Email UNIQUE (Email),
    CONSTRAINT CK_Users_UserName CHECK (
        LEN(UserName) BETWEEN 3 AND 50
        AND UserName COLLATE Latin1_General_BIN NOT LIKE N'%[^A-Za-z0-9._-]%')
);

-------------------------------------------------------------------------------
-- Masters: Clients > Projects > Modules / ProjectEnvironments / ProjectMembers
-------------------------------------------------------------------------------
CREATE TABLE dbo.Clients
(
    Id            int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Clients PRIMARY KEY,
    Name          nvarchar(150)      NOT NULL,
    ContactPerson nvarchar(100)      NULL,
    Email         nvarchar(256)      NULL,
    Phone         nvarchar(30)       NULL,
    IsActive      bit                NOT NULL,
    CreatedAt     datetimeoffset     NOT NULL,
    CreatedById   int                NOT NULL CONSTRAINT FK_Clients_CreatedBy REFERENCES dbo.Users (Id),
    UpdatedAt     datetimeoffset     NULL,
    UpdatedById   int                NULL CONSTRAINT FK_Clients_UpdatedBy REFERENCES dbo.Users (Id),
    RowVersion    rowversion         NOT NULL,
    CONSTRAINT UX_Clients_Name UNIQUE (Name)
);

CREATE TABLE dbo.Projects
(
    Id                      int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
    ClientId                int                NOT NULL CONSTRAINT FK_Projects_Client REFERENCES dbo.Clients (Id),
    Name                    nvarchar(150)      NOT NULL,
    -- Prefix of issue keys (PGERP-142): 2–8 characters, upper-case letter first, then upper-case letters/digits.
    ProjectKey              nvarchar(8)        NOT NULL,
    NextIssueNumber         int                NOT NULL,
    RequireLiveVerification bit                NOT NULL,
    IsActive                bit                NOT NULL,
    CreatedAt               datetimeoffset     NOT NULL,
    CreatedById             int                NOT NULL CONSTRAINT FK_Projects_CreatedBy REFERENCES dbo.Users (Id),
    UpdatedAt               datetimeoffset     NULL,
    UpdatedById             int                NULL CONSTRAINT FK_Projects_UpdatedBy REFERENCES dbo.Users (Id),
    RowVersion              rowversion         NOT NULL,
    CONSTRAINT UX_Projects_ProjectKey UNIQUE (ProjectKey),
    CONSTRAINT UX_Projects_Client_Name UNIQUE (ClientId, Name),
    CONSTRAINT CK_Projects_ProjectKey CHECK (
        LEN(ProjectKey) BETWEEN 2 AND 8
        AND ProjectKey COLLATE Latin1_General_BIN LIKE N'[A-Z]%'
        AND ProjectKey COLLATE Latin1_General_BIN NOT LIKE N'%[^A-Z0-9]%'),
    CONSTRAINT CK_Projects_NextIssueNumber CHECK (NextIssueNumber >= 1)
);

CREATE TABLE dbo.ProjectMembers
(
    ProjectId int            NOT NULL CONSTRAINT FK_ProjectMembers_Project REFERENCES dbo.Projects (Id),
    UserId    int            NOT NULL CONSTRAINT FK_ProjectMembers_User REFERENCES dbo.Users (Id),
    AddedAt   datetimeoffset NOT NULL,
    CONSTRAINT PK_ProjectMembers PRIMARY KEY (ProjectId, UserId)
);

CREATE TABLE dbo.Modules
(
    Id          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Modules PRIMARY KEY,
    ProjectId   int                NOT NULL CONSTRAINT FK_Modules_Project REFERENCES dbo.Projects (Id),
    Name        nvarchar(150)      NOT NULL,
    IsActive    bit                NOT NULL,
    CreatedAt   datetimeoffset     NOT NULL,
    CreatedById int                NOT NULL CONSTRAINT FK_Modules_CreatedBy REFERENCES dbo.Users (Id),
    UpdatedAt   datetimeoffset     NULL,
    UpdatedById int                NULL CONSTRAINT FK_Modules_UpdatedBy REFERENCES dbo.Users (Id),
    RowVersion  rowversion         NOT NULL,
    CONSTRAINT UX_Modules_Project_Name UNIQUE (ProjectId, Name)
);

-- Plan name "Environments"; renamed so the entity is ProjectEnvironment, not System.Environment (ADR-010).
CREATE TABLE dbo.ProjectEnvironments
(
    Id          int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ProjectEnvironments PRIMARY KEY,
    ProjectId   int                NOT NULL CONSTRAINT FK_ProjectEnvironments_Project REFERENCES dbo.Projects (Id),
    Name        nvarchar(100)      NOT NULL,
    Type        nvarchar(10)       NOT NULL CONSTRAINT CK_ProjectEnvironments_Type CHECK (Type IN (N'UAT', N'LIVE', N'Other')),
    Url         nvarchar(500)      NOT NULL,
    IsActive    bit                NOT NULL,
    CreatedAt   datetimeoffset     NOT NULL,
    CreatedById int                NOT NULL CONSTRAINT FK_ProjectEnvironments_CreatedBy REFERENCES dbo.Users (Id),
    UpdatedAt   datetimeoffset     NULL,
    UpdatedById int                NULL CONSTRAINT FK_ProjectEnvironments_UpdatedBy REFERENCES dbo.Users (Id),
    RowVersion  rowversion         NOT NULL,
    CONSTRAINT UX_ProjectEnvironments_Project_Name UNIQUE (ProjectId, Name)
);

-------------------------------------------------------------------------------
-- Testing sprints
-------------------------------------------------------------------------------
CREATE TABLE dbo.SprintPhases
(
    Id        int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_SprintPhases PRIMARY KEY,
    Name      nvarchar(50)       NOT NULL,
    SortOrder int                NOT NULL,
    IsActive  bit                NOT NULL,
    CONSTRAINT UX_SprintPhases_Name UNIQUE (Name)
);

CREATE TABLE dbo.TestingSprints
(
    Id                   int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_TestingSprints PRIMARY KEY,
    ProjectId            int                NOT NULL CONSTRAINT FK_TestingSprints_Project REFERENCES dbo.Projects (Id),
    Name                 nvarchar(150)      NOT NULL,
    PhaseId              int                NOT NULL CONSTRAINT FK_TestingSprints_Phase REFERENCES dbo.SprintPhases (Id),
    DefaultEnvironmentId int                NULL CONSTRAINT FK_TestingSprints_DefaultEnvironment REFERENCES dbo.ProjectEnvironments (Id),
    Status               nvarchar(20)       NOT NULL CONSTRAINT CK_TestingSprints_Status CHECK (Status IN (N'Testing', N'TestingCompleted', N'Closed')),
    StartedAt            datetimeoffset     NOT NULL,
    StartedById          int                NOT NULL CONSTRAINT FK_TestingSprints_StartedBy REFERENCES dbo.Users (Id),
    TestingCompletedAt   datetimeoffset     NULL,
    TestingCompletedById int                NULL CONSTRAINT FK_TestingSprints_TestingCompletedBy REFERENCES dbo.Users (Id),
    ClosedAt             datetimeoffset     NULL,
    ClosedById           int                NULL CONSTRAINT FK_TestingSprints_ClosedBy REFERENCES dbo.Users (Id),
    Notes                nvarchar(2000)     NULL,
    RowVersion           rowversion         NOT NULL,
    CONSTRAINT UX_TestingSprints_Project_Name UNIQUE (ProjectId, Name)
);

-- Plan 6.1: at most one sprint in Testing per project.
CREATE UNIQUE INDEX UX_TestingSprints_OneTestingPerProject ON dbo.TestingSprints (ProjectId) WHERE Status = N'Testing';
CREATE INDEX IX_TestingSprints_Project_Status ON dbo.TestingSprints (ProjectId, Status);

CREATE TABLE dbo.SprintEvents
(
    Id       int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_SprintEvents PRIMARY KEY,
    SprintId int                NOT NULL CONSTRAINT FK_SprintEvents_Sprint REFERENCES dbo.TestingSprints (Id),
    UserId   int                NOT NULL CONSTRAINT FK_SprintEvents_User REFERENCES dbo.Users (Id),
    Kind     nvarchar(30)       NOT NULL CONSTRAINT CK_SprintEvents_Kind CHECK (Kind IN (N'Started', N'TestingCompleted', N'ReopenedForTesting', N'Closed')),
    Note     nvarchar(1000)     NULL,
    At       datetimeoffset     NOT NULL
);

CREATE INDEX IX_SprintEvents_Sprint_At ON dbo.SprintEvents (SprintId, At);

-------------------------------------------------------------------------------
-- Issues
-------------------------------------------------------------------------------
CREATE TABLE dbo.Issues
(
    Id                   int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Issues PRIMARY KEY,
    ProjectId            int                NOT NULL CONSTRAINT FK_Issues_Project REFERENCES dbo.Projects (Id),
    Number               int                NOT NULL,
    -- "PGERP-142": ProjectKey + '-' + Number, written once at creation (plan "Key (computed)" – ADR-010).
    IssueKey             nvarchar(20)       NOT NULL,
    SprintId             int                NOT NULL CONSTRAINT FK_Issues_Sprint REFERENCES dbo.TestingSprints (Id),
    CarriedFromSprintId  int                NULL CONSTRAINT FK_Issues_CarriedFromSprint REFERENCES dbo.TestingSprints (Id),
    ModuleId             int                NOT NULL CONSTRAINT FK_Issues_Module REFERENCES dbo.Modules (Id),
    ScreenName           nvarchar(150)      NULL,
    FoundOnEnvironmentId int                NULL CONSTRAINT FK_Issues_FoundOnEnvironment REFERENCES dbo.ProjectEnvironments (Id),
    PageUrl              nvarchar(1000)     NULL,
    Type                 nvarchar(20)       NOT NULL CONSTRAINT CK_Issues_Type CHECK (Type IN (N'Bug', N'Validation', N'UI', N'Security', N'Performance', N'Enhancement', N'Clarification')),
    Title                nvarchar(200)      NOT NULL,
    Steps                nvarchar(max)      NULL,
    ExpectedResult       nvarchar(max)      NULL,
    ActualResult         nvarchar(max)      NOT NULL,
    Impact               nvarchar(max)      NULL,
    Severity             nvarchar(20)       NOT NULL CONSTRAINT CK_Issues_Severity CHECK (Severity IN (N'Critical', N'High', N'Medium', N'Low', N'NotAssessed')),
    Priority             nvarchar(10)       NOT NULL CONSTRAINT CK_Issues_Priority CHECK (Priority IN (N'High', N'Medium', N'Low')),
    Status               nvarchar(20)       NOT NULL CONSTRAINT CK_Issues_Status CHECK (Status IN (N'New', N'Assigned', N'InProgress', N'NeedInfo', N'OnHold', N'Disputed', N'Fixed', N'Verified', N'Closed', N'Reopened', N'Rejected')),
    HoldReason           nvarchar(1000)     NULL,
    RejectType           nvarchar(20)       NULL CONSTRAINT CK_Issues_RejectType CHECK (RejectType IN (N'Invalid', N'Duplicate', N'AsDesigned', N'WontFix')),
    RejectReason         nvarchar(1000)     NULL,
    DuplicateOfIssueId   int                NULL CONSTRAINT FK_Issues_DuplicateOfIssue REFERENCES dbo.Issues (Id),
    ReporterId           int                NOT NULL CONSTRAINT FK_Issues_Reporter REFERENCES dbo.Users (Id),
    AssigneeId           int                NULL CONSTRAINT FK_Issues_Assignee REFERENCES dbo.Users (Id),
    DueDate              datetimeoffset     NULL,
    ReopenCount          int                NOT NULL,
    CreatedAt            datetimeoffset     NOT NULL,
    UpdatedAt            datetimeoffset     NULL,
    FirstFixedAt         datetimeoffset     NULL,
    VerifiedAt           datetimeoffset     NULL,
    ClosedAt             datetimeoffset     NULL,
    RowVersion           rowversion         NOT NULL,
    CONSTRAINT UX_Issues_IssueKey UNIQUE (IssueKey),
    CONSTRAINT UX_Issues_Project_Number UNIQUE (ProjectId, Number),
    CONSTRAINT CK_Issues_ReopenCount CHECK (ReopenCount >= 0)
);

CREATE INDEX IX_Issues_Project_Status ON dbo.Issues (ProjectId, Status);
CREATE INDEX IX_Issues_Sprint_Status ON dbo.Issues (SprintId, Status);
CREATE INDEX IX_Issues_Assignee_Status ON dbo.Issues (AssigneeId, Status);
CREATE INDEX IX_Issues_Module_ScreenName ON dbo.Issues (ModuleId, ScreenName);

CREATE TABLE dbo.DeployNotices
(
    Id            int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DeployNotices PRIMARY KEY,
    ProjectId     int                NOT NULL CONSTRAINT FK_DeployNotices_Project REFERENCES dbo.Projects (Id),
    EnvironmentId int                NOT NULL CONSTRAINT FK_DeployNotices_Environment REFERENCES dbo.ProjectEnvironments (Id),
    CreatedById   int                NOT NULL CONSTRAINT FK_DeployNotices_CreatedBy REFERENCES dbo.Users (Id),
    CreatedAt     datetimeoffset     NOT NULL,
    Note          nvarchar(2000)     NULL
);

CREATE TABLE dbo.Deployments
(
    Id             int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Deployments PRIMARY KEY,
    IssueId        int                NOT NULL CONSTRAINT FK_Deployments_Issue REFERENCES dbo.Issues (Id) ON DELETE CASCADE,
    EnvironmentId  int                NOT NULL CONSTRAINT FK_Deployments_Environment REFERENCES dbo.ProjectEnvironments (Id),
    DeployNoticeId int                NULL CONSTRAINT FK_Deployments_DeployNotice REFERENCES dbo.DeployNotices (Id),
    DeployedById   int                NOT NULL CONSTRAINT FK_Deployments_DeployedBy REFERENCES dbo.Users (Id),
    DeployedAt     datetimeoffset     NOT NULL,
    Note           nvarchar(2000)     NULL,
    Version        nvarchar(100)      NULL,
    RetestResult   nvarchar(10)       NOT NULL CONSTRAINT CK_Deployments_RetestResult CHECK (RetestResult IN (N'Pending', N'Passed', N'Failed')),
    RetestedById   int                NULL CONSTRAINT FK_Deployments_RetestedBy REFERENCES dbo.Users (Id),
    RetestedAt     datetimeoffset     NULL,
    RetestComment  nvarchar(2000)     NULL
);

CREATE INDEX IX_Deployments_RetestResult_Environment ON dbo.Deployments (RetestResult, EnvironmentId);
CREATE INDEX IX_Deployments_Issue ON dbo.Deployments (IssueId);

-------------------------------------------------------------------------------
-- Timeline, mentions, attachments, watchers, links
-------------------------------------------------------------------------------
CREATE TABLE dbo.IssueActivities
(
    Id            int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_IssueActivities PRIMARY KEY,
    IssueId       int                NOT NULL CONSTRAINT FK_IssueActivities_Issue REFERENCES dbo.Issues (Id) ON DELETE CASCADE,
    -- NULL = system (background job) event.
    UserId        int                NULL CONSTRAINT FK_IssueActivities_User REFERENCES dbo.Users (Id),
    Kind          nvarchar(20)       NOT NULL CONSTRAINT CK_IssueActivities_Kind CHECK (Kind IN (N'Created', N'Comment', N'StatusChanged', N'Assigned', N'FieldChanged', N'Deployed', N'RetestPassed', N'RetestFailed', N'Attachment', N'CarriedForward', N'SprintEvent')),
    Body          nvarchar(max)      NULL,
    OldValue      nvarchar(500)      NULL,
    NewValue      nvarchar(500)      NULL,
    EnvironmentId int                NULL CONSTRAINT FK_IssueActivities_Environment REFERENCES dbo.ProjectEnvironments (Id),
    DeploymentId  int                NULL CONSTRAINT FK_IssueActivities_Deployment REFERENCES dbo.Deployments (Id),
    CreatedAt     datetimeoffset     NOT NULL,
    EditedAt      datetimeoffset     NULL
);

CREATE INDEX IX_IssueActivities_Issue_CreatedAt ON dbo.IssueActivities (IssueId, CreatedAt);

CREATE TABLE dbo.Mentions
(
    Id         int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Mentions PRIMARY KEY,
    ActivityId int                NOT NULL CONSTRAINT FK_Mentions_Activity REFERENCES dbo.IssueActivities (Id) ON DELETE CASCADE,
    UserId     int                NOT NULL CONSTRAINT FK_Mentions_User REFERENCES dbo.Users (Id),
    CONSTRAINT UX_Mentions_Activity_User UNIQUE (ActivityId, UserId)
);

CREATE TABLE dbo.Attachments
(
    Id            int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Attachments PRIMARY KEY,
    IssueId       int                NOT NULL CONSTRAINT FK_Attachments_Issue REFERENCES dbo.Issues (Id) ON DELETE CASCADE,
    ActivityId    int                NULL CONSTRAINT FK_Attachments_Activity REFERENCES dbo.IssueActivities (Id),
    FileName      nvarchar(255)      NOT NULL,
    ContentType   nvarchar(100)      NOT NULL,
    SizeBytes     bigint             NOT NULL,
    StoragePath   nvarchar(500)      NOT NULL,
    ThumbnailPath nvarchar(500)      NULL,
    UploadedById  int                NOT NULL CONSTRAINT FK_Attachments_UploadedBy REFERENCES dbo.Users (Id),
    UploadedAt    datetimeoffset     NOT NULL,
    CONSTRAINT CK_Attachments_SizeBytes CHECK (SizeBytes >= 0)
);

CREATE INDEX IX_Attachments_Issue ON dbo.Attachments (IssueId);

CREATE TABLE dbo.IssueWatchers
(
    IssueId int            NOT NULL CONSTRAINT FK_IssueWatchers_Issue REFERENCES dbo.Issues (Id) ON DELETE CASCADE,
    UserId  int            NOT NULL CONSTRAINT FK_IssueWatchers_User REFERENCES dbo.Users (Id),
    AddedAt datetimeoffset NOT NULL,
    CONSTRAINT PK_IssueWatchers PRIMARY KEY (IssueId, UserId)
);

CREATE TABLE dbo.IssueLinks
(
    FromIssueId int            NOT NULL CONSTRAINT FK_IssueLinks_FromIssue REFERENCES dbo.Issues (Id) ON DELETE CASCADE,
    -- No cascade (SQL Server forbids two cascade paths); the delete handler removes inbound links first.
    ToIssueId   int            NOT NULL CONSTRAINT FK_IssueLinks_ToIssue REFERENCES dbo.Issues (Id),
    Type        nvarchar(20)   NOT NULL CONSTRAINT CK_IssueLinks_Type CHECK (Type IN (N'Duplicate', N'Related')),
    CreatedAt   datetimeoffset NOT NULL,
    CONSTRAINT PK_IssueLinks PRIMARY KEY (FromIssueId, ToIssueId),
    CONSTRAINT CK_IssueLinks_NotSelf CHECK (FromIssueId <> ToIssueId)
);

-------------------------------------------------------------------------------
-- Notifications and e-mail
-------------------------------------------------------------------------------
CREATE TABLE dbo.Notifications
(
    Id        int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
    UserId    int                NOT NULL CONSTRAINT FK_Notifications_User REFERENCES dbo.Users (Id),
    Type      nvarchar(30)       NOT NULL CONSTRAINT CK_Notifications_Type CHECK (Type IN (N'TestingCompleted', N'Assigned', N'Mentioned', N'Fixed', N'Reopened', N'NeedInfo', N'Disputed', N'Comment', N'CriticalUnassigned', N'Overdue')),
    IssueId   int                NULL CONSTRAINT FK_Notifications_Issue REFERENCES dbo.Issues (Id) ON DELETE CASCADE,
    SprintId  int                NULL CONSTRAINT FK_Notifications_Sprint REFERENCES dbo.TestingSprints (Id),
    Title     nvarchar(200)      NOT NULL,
    Body      nvarchar(1000)     NULL,
    IsRead    bit                NOT NULL,
    CreatedAt datetimeoffset     NOT NULL
);

CREATE INDEX IX_Notifications_User_IsRead_CreatedAt ON dbo.Notifications (UserId, IsRead, CreatedAt);

CREATE TABLE dbo.NotificationPreferences
(
    UserId int          NOT NULL CONSTRAINT FK_NotificationPreferences_User REFERENCES dbo.Users (Id),
    Type   nvarchar(30) NOT NULL CONSTRAINT CK_NotificationPreferences_Type CHECK (Type IN (N'TestingCompleted', N'Assigned', N'Mentioned', N'Fixed', N'Reopened', N'NeedInfo', N'Disputed', N'Comment', N'CriticalUnassigned', N'Overdue')),
    Email  bit          NOT NULL,
    CONSTRAINT PK_NotificationPreferences PRIMARY KEY (UserId, Type)
);

CREATE TABLE dbo.EmailOutbox
(
    Id        int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_EmailOutbox PRIMARY KEY,
    -- Plan column "To" (reserved word in T-SQL).
    ToAddress nvarchar(256)      NOT NULL,
    Subject   nvarchar(300)      NOT NULL,
    Body      nvarchar(max)      NOT NULL,
    CreatedAt datetimeoffset     NOT NULL,
    SentAt    datetimeoffset     NULL,
    Attempts  int                NOT NULL,
    LastError nvarchar(2000)     NULL
);

CREATE INDEX IX_EmailOutbox_Pending ON dbo.EmailOutbox (CreatedAt) WHERE SentAt IS NULL;

-------------------------------------------------------------------------------
-- Settings, snapshots, saved views
-------------------------------------------------------------------------------
CREATE TABLE dbo.SlaPolicies
(
    Id           int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_SlaPolicies PRIMARY KEY,
    Severity     nvarchar(20)       NOT NULL CONSTRAINT CK_SlaPolicies_Severity CHECK (Severity IN (N'Critical', N'High', N'Medium', N'Low', N'NotAssessed')),
    WorkingHours int                NOT NULL CONSTRAINT CK_SlaPolicies_WorkingHours CHECK (WorkingHours > 0),
    CONSTRAINT UX_SlaPolicies_Severity UNIQUE (Severity)
);

CREATE TABLE dbo.Holidays
(
    [Date] date          NOT NULL CONSTRAINT PK_Holidays PRIMARY KEY,
    Name   nvarchar(100) NOT NULL
);

CREATE TABLE dbo.SprintSnapshots
(
    SprintId          int          NOT NULL CONSTRAINT FK_SprintSnapshots_Sprint REFERENCES dbo.TestingSprints (Id),
    IssueId           int          NOT NULL CONSTRAINT FK_SprintSnapshots_Issue REFERENCES dbo.Issues (Id),
    StatusAtClose     nvarchar(20) NOT NULL CONSTRAINT CK_SprintSnapshots_Status CHECK (StatusAtClose IN (N'New', N'Assigned', N'InProgress', N'NeedInfo', N'OnHold', N'Disputed', N'Fixed', N'Verified', N'Closed', N'Reopened', N'Rejected')),
    SeverityAtClose   nvarchar(20) NOT NULL CONSTRAINT CK_SprintSnapshots_Severity CHECK (SeverityAtClose IN (N'Critical', N'High', N'Medium', N'Low', N'NotAssessed')),
    AssigneeAtCloseId int          NULL CONSTRAINT FK_SprintSnapshots_AssigneeAtClose REFERENCES dbo.Users (Id),
    CONSTRAINT PK_SprintSnapshots PRIMARY KEY (SprintId, IssueId)
);

CREATE TABLE dbo.SavedViews
(
    Id         int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_SavedViews PRIMARY KEY,
    UserId     int                NOT NULL CONSTRAINT FK_SavedViews_User REFERENCES dbo.Users (Id),
    Name       nvarchar(100)      NOT NULL,
    Page       nvarchar(50)       NOT NULL,
    FilterJson nvarchar(max)      NOT NULL,
    CreatedAt  datetimeoffset     NOT NULL,
    CONSTRAINT UX_SavedViews_User_Page_Name UNIQUE (UserId, Page, Name)
);

COMMIT TRANSACTION;
