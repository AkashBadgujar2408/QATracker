# Architecture decisions

Short records of where the build deviates from (or refines) docs/plan.md. Newest last.

## ADR-001 – Database-first with Scaffold-DbContext (27-Oct-2026)
**Decision (requested by Akash):** The SQL Server schema is written by hand as SQL scripts in `database/scripts`
and the EF Core model is generated from the database with `Scaffold-DbContext`. The command is kept in
`database/scaffold-command.txt` so anyone can rerun or adjust it.
**Replaces:** plan 11.1 "Code-first migrations", 12 "migrations", 13.1 "configurations in IEntityTypeConfiguration /
migrations named YYYYMMDD_Description", 13.3 "never edit an applied migration", S03 "initial migration; apply on startup".
**Details:**
- Scripts are numbered `NNN_Description.sql`, idempotent where practical, never edited once applied.
- Scaffolded entities go to `QaTracker.Domain/Entities` (POCO `partial` classes, no EF reference in Domain);
  `QaTrackerDbContext` goes to `QaTracker.Infrastructure/Persistence`. Custom behaviour lives in separate partial files.
- Indexes, filtered unique index, rowversion and the computed issue Key are defined in SQL, not in C#.
- Seed data (admin user, phases, SLA defaults) stays an idempotent seeder (S03) – it may be a SQL script or C#; decide in S03.
**Consequence:** S03 becomes "write schema scripts + scaffold + seeder"; integration tests must create the DB by
running the scripts.

## ADR-002 – Connection string in appsettings.json (27-Oct-2026)
**Decision (requested by Akash):** `ConnectionStrings:QaTracker` lives in `src/QaTracker.Api/appsettings.json`.
Local dev uses Windows authentication (`.\SQLEXPRESS`), so the file contains no password.
Production can still override it with the environment variable `ConnectionStrings__QaTracker`; any SQL login
password must not be committed (plan 13.1 rule on secrets still applies to passwords, JWT keys, SMTP).

## ADR-003 – Local SQL Server Express instead of Docker (27-Oct-2026)
Docker is not installed on the dev machine; SQL Server 2025 Express (`.\SQLEXPRESS`) is. `docker-compose.yml` is kept
as an optional alternative. **Open question for S04:** plan 15.1 uses Testcontainers for integration tests, which needs
Docker. Proposed alternative: integration tests create a throw-away database (`QaTracker_Test_<guid>`) on `.\SQLEXPRESS`
by running `database/scripts`, and drop it afterwards.

## ADR-004 – xUnit v3 on Microsoft.Testing.Platform (27-Oct-2026)
.NET 10 SDK no longer runs MTP-based test projects through VSTest. `global.json` opts in to
`Microsoft.Testing.Platform`; test projects use `xunit.v3` (OutputType Exe) and no longer need
Microsoft.NET.Test.Sdk / xunit.runner.visualstudio / coverlet.collector. `dotnet test` works as usual.

## ADR-005 – Angular initial bundle budget 1 MB (27-Oct-2026)
PrimeNG + theme pushes the initial bundle just over the CLI default 500 kB warning. Budget raised to
1 MB warning / 1.5 MB error; bundle size is reviewed again in S31.

## ADR-006 – Transitive pin for System.Security.Cryptography.Pkcs (27-Oct-2026)
EF Core tooling pulls in Pkcs 9.0.11 (GHSA-2fmv-rv2c-59x7, GHSA-846f-f764-76mx). With warnings-as-errors this
fails the build, so `Directory.Packages.props` enables transitive pinning and pins 10.0.14.

## ADR-007 – Angular Material + CDK instead of PrimeNG (27-Oct-2026, S02)
**Finding:** PrimeNG ≥ 20, `@primeuix/themes` and primeicons 8 are no longer MIT – they ship under the commercial
"PrimeUI License" (licence key required; free Community tier only for organisations with < 5 developers and
< 10 employees). No MIT PrimeNG release supports Angular 22. S01 wrongly recorded PrimeNG as MIT.
**Decision (Akash):** use Angular Material 22 + CDK (MIT). PrimeNG packages removed.
**Replaces:** plan 10.3 "Components from PrimeNG", 11.1 "UI kit PrimeNG".
**Details:** M3 theme in `src/styles/material-theme.scss` – palettes generated from our token colours
(`ng generate @angular/material:theme-color` → `_theme-colors.scss`), then key system colours, corner radii and
button shapes mapped directly onto `--qa-*` tokens. Density -2. Toasts = MatSnackBar (ToastService),
confirm = MatDialog (ConfirmService + ConfirmDialog). Data table (S15) will be MatTable + CDK, tree (S07) MatTree.

## ADR-008 – Lucide icons via @ng-icons (27-Oct-2026, S02)
Icons are Lucide SVGs through `@ng-icons/core` + `@ng-icons/lucide` (MIT / ISC), registered per component with
`provideIcons` so only used icons are bundled. Material Symbols font was rejected (≈ 4 MB font file).

## ADR-009 – Custom authentication with plain-text passwords (27-Oct-2026, S03)
**Decision (Akash, after the risk was explained):** passwords are stored **unencrypted** in `Users.Password`
so they can be checked and read directly in the database ("internal application").
**Replaces:** plan 11.1 "ASP.NET Core Identity", 15.2 password handling. No Identity tables/stores; users,
roles and lockout live in our own `Users` table (Role column, AccessFailedCount, LockoutEndAt, SecurityStamp).
**Accepted risks:** anyone with database, backup or log access can read every password; reused passwords
leak other accounts; does not meet common audit / data-protection expectations.
**Mitigations kept:** passwords never logged or returned by the API; constant-time comparison; login rate limit +
lockout (S04); forced change on first login; initial admin password only via user-secrets/env, never in git;
DB backups must be access-restricted. Revisit before any external exposure – switching to hashing later needs
a new script (column rename) + forced password reset.

## ADR-010 – Schema naming refinements (27-Oct-2026, S03)
Plan section 12 with these deviations:
- `Environments` → **`ProjectEnvironments`** (entity `ProjectEnvironment`, avoids clashing with `System.Environment`).
- Issue key column **`IssueKey`** stored on insert (KEY is reserved; a computed column cannot read Projects.Key).
  Project key column **`ProjectKey`**. EmailOutbox `To` → **`ToAddress`**. DeployNotices `ById/At` → `CreatedById/CreatedAt`.
- Added: Users auth columns (UserName, Password, SecurityStamp, AccessFailedCount, LockoutEndAt, LastLoginAt);
  `AddedAt` on ProjectMembers/IssueWatchers, `Id` on Mentions, `CreatedAt` on IssueLinks/SavedViews (explicit entities
  instead of implicit many-to-many); audit columns + RowVersion on all masters and Users; `SprintSnapshots.AssigneeAtCloseId`.
- No DEFAULT constraints (EF sentinel pitfall with false/0); the application sets every value.
- Enums stored by name (nvarchar + CHECK); generated as C# enums by the customised `EntityType.t4` and converted
  in `QaTrackerDbContext.Conventions.cs`.
- Issue children cascade on delete (only allowed while the sprint is in Testing); `IssueLinks.ToIssueId` does not.
- SLA defaults = working days × 8 h (Critical 8, High 24, Medium 56, Low 120). `NotAssessed` has no SLA row yet –
  decide the due-date rule in S08.

## ADR-011 – Login with user name, not e-mail (27-Oct-2026, S03)
**Decision (Akash):** users log in with **UserName + password**. `Users.UserName` is unique (case-insensitive),
3–50 chars of letters, digits, `.`, `_`, `-`. E-mail stays required and unique but is used only for notifications.
**Replaces:** plan 8.2 Login "Email + password" (S04/S05 build the login on UserName).

## ADR-012 – Application layer conventions (27-Oct-2026, S04)
- Handlers use `IApplicationDbContext` (Application) which the scaffolded `QaTrackerDbContext` implements through a
  partial class. Application therefore references `Microsoft.EntityFrameworkCore` (abstractions/LINQ only – no provider).
- **One file per use case** (`Features/Auth/Login.cs` = command record + validator + handler) instead of four files
  (plan 11.2). Small master entities keep all their use cases in one file (`Features/Modules/Modules.cs`). DTOs shared by a feature live in one file (`Features/Users/UserDto.cs`).
- Handlers validate their own command first (`validator.CheckAsync`) and return `Result<T>`; endpoints map errors to
  ProblemDetails (`ResultHttpExtensions`, field names camelCase, `code` extension for the client).
- Handlers are registered by convention (class name ends with `Handler`).

## ADR-013 – Authentication details (27-Oct-2026, S04)
- JWT access token (15 min, HMAC-SHA256, key `Auth:SigningKey` from user-secrets/env) + opaque refresh token in an
  httpOnly, Secure, SameSite=Strict cookie `qa_refresh` limited to `/api/auth`; refresh tokens stored as SHA-256 hashes
  (`002_RefreshTokens.sql`), rotated on every refresh; replaying a rotated token revokes all of the user's sessions.
- Every request re-checks the user's `IsActive` and `SecurityStamp`; deactivation, password change/reset and role change
  replace the stamp, so old access tokens stop working immediately.
- Users with `MustChangePassword` get a `pwd_change` claim: only `/api/auth/me`, `/change-password`, `/logout` are allowed
  (policy `SignedIn`); every other endpoint uses the default/fallback policy `Authenticated` (signed in + password changed).
- Lockout: 5 wrong passwords → 15 minutes. Login rate limit: 10 requests/minute per IP (configurable).
- Admin cannot deactivate themselves; the last active admin cannot be deactivated or lose the Admin role.
- User name is immutable after creation.

## ADR-014 – Visual identity and readability (27-Oct-2026, S05)
Requested by Akash during S05 review:
- **Type one step larger** than plan 10.2: 13 / 14 / **15 (body)** / 17 / 21 / 26 / 32 px; Material system type and
  density (-1) matched. Sidebar items 16 px, sub-items 15 px.
- **Dates on the UI: `dd/MMM/yyyy`** (27/Oct/2026, with time 27/Oct/2026 18:00), IST – replaces plan "dd-MMM-yyyy".
- **App mark** (`app-logo`, `public/favicon.svg`): an inspection lens with a check inside. Angular's favicon removed.
- **Ink sidebar** (slate-900, slate-950 in dark mode) with an indigo active bar; Dashboards submenu animates open
  (grid-rows transition, respects reduced motion) and sits indented on a guide line.
- **Sign-in screen** split: brand panel showing one issue's journey (New → In Progress → Fixed → Verified) next to the
  form. Sample people on public-facing screens use neutral names (John, Emily …), never real staff.
- List pages put their filters in a toolbar on top of the table card; Material table header/row tokens set globally.
- Icons: ng-icon is centred (not baseline-aligned) and Material icon buttons keep the svg at the ng-icon size.

## ADR-015 – Masters API shape (27-Oct-2026, S06)
- Plan 14 routes, refined: projects are created/listed under `/api/clients/{clientId}/projects` but edited at
  `PUT /api/projects/{id}`; modules/environments are created under `/api/projects/{id}/…` and edited at
  `/api/modules/{id}` / `/api/environments/{id}`; every master has `PATCH …/{id}/active`.
- Members: `PUT /api/projects/{id}/members {userIds}` replaces the whole set (fits a multi-select); only active users can be added.
- Quick-add = the normal POST with only the required fields; it returns the created item (201) for the picker.
- `GET /api/lookups/similar?kind=&name=&parentId=`: same-scope names that match ignoring case/spaces/punctuation, contain each
  other, or are a few edits apart (typos); `exact` marks a would-be duplicate. `GET /api/lookups/project-key` suggests a free key
  (client initials + project initials/short name, number suffix when taken).
- Rules: project key 2–8 chars, letter first, stored upper-case, globally unique, **locked once the project has issues**;
  inactive clients/projects cannot get new children (422). Deactivation does not cascade – the cascade lookup hides inactive items.
- Reads: any signed-in user (developers view masters); writes: Tester or Admin.

## ADR-016 – SLA and due dates (27-Oct-2026)
**Decision (Akash):**
- Severity is **required** on every issue (no default in the form); "Not assessed" is a valid choice (also used by the
  legacy Excel import for "Not yet assessed").
- SLA hours per severity are **admin-configurable** (Settings, S28). Seeded **defaults** (changed by Akash, replacing plan 8.3): **Critical 7,
  High 14, Medium 21, Low 28, Not assessed 35 working days** (8 working hours/day; working days skip Sundays and holidays).
- Due date = TestingCompletedAt + SLA(severity); the tester may edit it.
- Pending confirmation: when severity changes after testing completed, recalculate the due date unless the tester set it
  by hand (default assumption for S08/S13).

## ADR-017 – Testing sprint API details (28-Oct-2026, S08)
- Rules live in `Domain/Rules/SprintRules` and return a `RuleCheck` (allowed / code / reason); the Application maps a
  denied check to 403 when the role is wrong (`role_not_allowed`) and 422 otherwise (`RuleCheckExtensions.ToError`).
- Routes (plan 14, refined): `GET/POST /api/projects/{id}/sprints`, `GET /api/projects/{id}/sprints/suggested-name?phaseId=`,
  `GET /api/sprints/{id}` (with the sprint history), `GET /api/sprints/{id}/completion-summary` (the confirmation summary of
  plan 6.1), `POST /api/sprints/{id}/complete-testing {rowVersion, note?}`, `POST /api/sprints/{id}/reopen-testing
  {rowVersion, note}` (admin), `GET /api/lookups/phases`.
- Name suggestion: the number of the newest "Sprint N …" sprint (1 if none) + phase, then "Round 2, 3 …" when taken.
- Due dates (`WorkingCalendar`, ADR-016): days counted in IST; every 8 SLA hours moves to the next working day (Sundays and
  `Holidays` skipped) at the same time of day; leftover hours are clock hours; a due time on a non-working day moves to the
  next working day. Stored in UTC. Closed/Rejected issues get none.
- Completing testing writes, in one transaction: sprint status/TestingCompletedAt/By, a `SprintEvents` row, a `SprintEvent`
  activity per issue (old/new due date), and an in-app `Notifications` row (type TestingCompleted) for active project members
  and assignees except the actor. E-mail delivery is left to the notifications sessions.
- Reopen testing clears TestingCompletedAt/By and keeps issue due dates; completing again recalculates them (overwrites
  dates a tester edited by hand – revisit with S13).

## ADR-018 – Issue API details (28-Oct-2026, S09)
- Numbering: `IIssueNumbering` (Infrastructure) takes `sp_getapplock` per project inside a transaction, then MAX(Number)+1;
  `UX_Issues_Project_Number` stays the last guard. Deleting the newest issue lets its number be reused (only possible
  while the sprint is in Testing).
- Rich text (steps, expected, actual, impact) is sanitised on write with HtmlSanitizer (Ganss.Xss, MIT) behind
  `IRichTextSanitizer`: p, br, strong/b, em/i, u, s, lists, links (http/https/mailto, rel=noopener), code, pre, blockquote,
  h3/h4. No images, styles or event handlers. Actual result must keep visible text.
- Type, severity and priority are nullable in requests and required by validation (no silent enum default – ADR-016).
- Update = `PUT /api/issues/{key}` with `If-Match: "<rowVersion>"` (ETag from GET/POST/PUT); missing → 428, stale → 409.
  Each changed field writes a FieldChanged activity (rich text: "edited", no values); assignee changes write Assigned
  activities with user ids and move New ⇄ Assigned.
- Assignee must be an active non-tester (developer or admin). Module/environment must be active unless unchanged.
- Delete removes inbound `IssueLinks` and clears `DuplicateOfIssueId` references first; the rest cascades.
- List: `GET /api/issues` with plan 14 filters (repeat status/severity/priority for several values, `unassigned`,
  `overdue` = due date passed and still awaiting a fix), `q` on key/title/screen, `page`/`size` (≤200),
  `sort` = field or `-field`; severity/priority sort by importance. `GET /api/projects/{id}/screens` suggests screens.
- `IssueDto.permissions` (canEdit/canDelete) until IssueWorkflow's `allowedActions` arrives in S12/S13.

## ADR-019 – E-mail, scheduled jobs and a jobs admin app (28-Oct-2026) – PROPOSED, to confirm before S19
**Request (Akash, during S09):** after the planned sessions, add SMTP mail: set-level mails to members (sprint started,
testing completed, SLA/overdue …) sent as they happen; per-issue events collected into a **daily mail at 18:00 IST**;
a background job framework (Hangfire or better); an **MVC front end to manage background jobs without delete options**;
new alerts/mails and background tasks configurable in the app; one database for both (or two, whichever fits better).

**Recommendation (to be confirmed):**
- Keep the plan's `EmailOutbox` (S19) as the single place mails are queued, in the same transaction as the change.
- Scheduler: **Hangfire** (Hangfire.Core LGPL-3.0, used unmodified as a NuGet package; SQL Server storage) – recurring jobs
  with cron + time zone ("0 18 * * *", India Standard Time), retries, history. Alternative: Quartz.NET (Apache-2.0).
- **One database**: Hangfire tables live in their own `HangFire` schema of the QaTracker database (one backup, one
  connection string, jobs can read the outbox directly). Our schema scripts ignore that schema; Hangfire creates it.
- **Jobs admin = separate ASP.NET Core MVC project `QaTracker.Jobs`** (server-rendered, admin sign-in against the same
  Users table) that hosts the Hangfire server and our own management pages: list recurring jobs, last/next run, history,
  "run now", pause/resume, edit schedule – **no delete**. The Hangfire dashboard, if exposed at all, runs read-only.
- **Configurable alerts**: new tables (e.g. `AlertRules`: event, audience, channel = instant/digest, schedule, enabled)
  edited from Settings (S28) or the jobs admin; jobs read them at run time, so adding/changing an alert needs no deploy.
- Sessions affected: S19 (outbox, sender, set-level mails), new S19b (daily digest + alert rules), S28 (SMTP settings,
  alerts UI), new S28b (QaTracker.Jobs MVC admin). Exact split to agree before S19.

## ADR-020 – Attachments: storage, checks, thumbnails, signed URLs (28-Oct-2026, S10)
- **Storage** (`IFileStorage` → `LocalFileStorage`): folder from `Storage:Root` in appsettings.json (default
  `App_Data/attachments` under the API content root; absolute paths allowed, `Storage__Root` per environment). Never
  `wwwroot`. Files are `yyyy/MM/<guid>.<ext>` (+ `<guid>_thumb.webp`); the user's file name is only in the database.
  Back the folder up with the database. Paths are generated by us and checked to stay inside the root.
- **Checks** (`FileInspector`): PNG, JPEG, GIF, WebP, PDF, DOCX, XLSX, TXT/LOG; max 10 MB each, 1–5 files per upload,
  all-or-nothing. Type from the content (magic bytes; Office zips must contain `word/document.xml` / `xl/workbook.xml`;
  text must be UTF-8 without NULs) and the extension must agree. File names are cleaned (no folders/control chars).
- **Thumbnails** (`IThumbnailGenerator` → SkiaSharp, MIT): WebP, longest side 320 px, EXIF orientation applied, images
  over 50 megapixels are not decoded. Used by galleries, lists and later the sprint report (S25).
- **Download** (`GET /api/attachments/{id}` and `/thumbnail`): bearer token, or a signed URL from the list
  (`?u=&exp=&sig=`, HMAC-SHA256 with a key derived from `Auth:SigningKey`, valid 1 hour, user must still be active).
  Bad/expired signature → 403, no credentials → 401. Responses: nosniff, `sandbox` CSP, private cache; images, PDF and
  text inline, Office files as downloads.
- **Rules**: upload/remove = tester/admin while the sprint is in Testing (comment attachments come with S14). Each upload
  writes an Attachment activity; removal writes one too. Deleting an issue deletes its files after the database delete.
- `004_IssueDeleteIndexes.sql`: indexes on the foreign keys an issue delete checks (IssueLinks.ToIssueId,
  Notifications.IssueId, Issues.DuplicateOfIssueId, SprintSnapshots.IssueId) – without them deletes scanned those tables.

## ADR-021 – Testing workspace layout and behaviour (28-Oct-2026, S11)
- **One place to log issues** (confirmed with Akash): the tester picks or starts the sprint inline on the Testing screen and
  logs issues straight into it – there is no separate sprint screen for adding issues. The Sprints page (S29) links here
  with `/testing?client=&project=&sprint=` ("Log issues"); a link wins over the remembered selection.
- **Form ≈ 60 %, list ≈ 40 %** (plan 8.1 said 40/60) – Akash found the form too narrow for steps/expected/actual text.
  Rich-text editors are taller (steps 5 lines) and can be dragged taller.
- "Mark testing completed" sits in the context bar next to the sprint's stats (not the page header). Once testing is
  completed the form, edit/clone/delete are read-only with a banner and "Start new sprint" (phase/environment carried over).
- Save bar is sticky and pinned flush to the bottom of the screen (offset cancels the page's bottom padding).
- Selection (client/project/sprint) and "group by module" are remembered per user in localStorage (browser only).
- Rich text: own small `app-rich-text-editor` (contenteditable + execCommand: bold, italic, underline, lists, code), no
  library. Pasted text is inserted as plain text; pasted images go to the page-wide paste-dropzone. Values written into
  the editor go through Angular's sanitizer; the API sanitises again (ADR-018).
- Stats chips are computed in the browser from the sprint's issues (≤ 200, one page): total, today (IST), by severity,
  "Day N" = distinct IST dates with issues, plus today while in Testing.
- Enter in an entity-picker never submits the surrounding form; with exactly one match it picks it.

## ADR-022 – IssueWorkflow (28-Oct-2026, S12)
- `Domain/Rules/IssueWorkflow` holds every transition of plan 7.2 as one table: action → who, from-statuses, target,
  required inputs, assignee rule. `Check` returns a `RuleCheck`; `AllowedActions` lists the allowed transitions.
- Actions: Assign, Pick, Start, NeedInfo, Resume, Hold, Unhold, Dispute, Reject, Contest (tester disagrees → Assigned),
  MarkFixed (InProgress/Fixed/Verified → Fixed: also "add a LIVE deployment"), Verify, Reopen (Fixed/Closed), Close.
- Refinements of the plan table: admins may do everything; developer actions on an assigned issue (need info, hold,
  dispute, fixed, resume, unhold) need the developer to be the assignee – Pick is for unassigned issues, Start for the
  assignee; Assign (reassign) is possible in every status except Closed/Rejected; a closed sprint allows no action.
- During Testing only the tester's/admin's Assign (pre-assignment) is allowed; developers get `sprint_in_testing`.
- Check order (first failure wins): sprint closed → role → sprint in testing → status → assignee → LIVE verification.
  Codes: sprint_closed, role_not_allowed, sprint_in_testing, invalid_transition, not_assignee, already_assigned,
  live_verification_required (role_not_allowed → 403, the rest → 422 via RuleCheckExtensions).
- `IssueDto.allowedActions` = [{action, target, requires[]}] for the current user (GET/POST/PUT).

## ADR-023 – Workflow API (28-Oct-2026, S13)
- Routes (plan 14): `POST /api/issues/{key}/assign {userId, note}`, `/pick`, `/transition {action, reason, rejectType,
  duplicateOfKey}` (Start, NeedInfo, Resume, Hold, Unhold, Dispute, Reject, Contest, Verify, Reopen, Close),
  `/fixed {note, environmentIds[], version}`, `POST /api/issues/bulk-assign {keys[], userId, note}`,
  `POST /api/deployments/{id}/retest {passed, comment}`. All return the issue (with ETag and allowedActions) except
  bulk assign ({assigned[], failed[{key, code, reason}]}).
- Single-issue actions need `If-Match` (428 without, 409 stale) – the same rule as `PUT`. Pick needs none: it is one
  `UPDATE … WHERE AssigneeId IS NULL AND Status = @from` (ExecuteUpdate) in a transaction with its activities; zero rows →
  409 `already_picked` "Already picked by X". Retest is guarded by "deployment still pending" plus the issue rowversion.
- Every endpoint is open to signed-in users; IssueWorkflow decides (403 role, 422 rule). Inputs are validated (400):
  reason where the action needs one, reject type, duplicate key, fix note and ≥ 1 active environment of the project,
  failed-retest comment.
- Activities: Assigned (old/new user id, note in Body; a pick = assigned from nobody to oneself), StatusChanged
  (old/new status, reason in Body), Comment + Mention of the reporter for Need info (the question), Deployed per
  environment (note, version in NewValue), RetestPassed/RetestFailed per deployment. Display text ("X assigned to Y",
  "X picked") is rendered by the timeline (S14) from these ids.
- Side effects: Hold sets/clears HoldReason; Reject stores type/reason and, for Duplicate, DuplicateOfIssueId plus an
  IssueLink; Fixed sets FirstFixedAt once and clears VerifiedAt; Verify (or a passed retest with no other pending one)
  sets VerifiedAt and passes pending deployments; Reopen +1 ReopenCount, fails pending deployments, clears
  VerifiedAt/ClosedAt; Close sets ClosedAt.
- No notifications yet (assigned, reopened, need info …) – they come with S18/S19 from these activities.

## ADR-024 – Timeline and comments (28-Oct-2026, S14)
- `GET /api/issues/{key}/activities?before=&size=&filter=All|Comments|History` → `{items (oldest first), hasMore}`.
  The first call returns the newest page; older pages with `before` = the first item's id (ids follow time). Size ≤ 200.
- Each entry: kind, user name/avatar colour, `summary` (plain English, e.g. "Akash assigned to Nitin", "Nitin picked the
  issue", "Akash: retest failed on UAT 1"), raw body/old/new values, environment, `canEdit`, mentions and – for
  comments – its attachments (signed URLs). Names in summaries come from user ids at read time.
- Comments: `POST /api/issues/{key}/comments` as multipart (`body`, up to 5 `files`, same checks as attachments) –
  every role, every sprint state (also Closed and, for developers, while in Testing). Plain text, not HTML; the UI
  escapes it. Mentions = `@user.name` (ADR-011 user names) of active users, not oneself; stored in `Mentions`.
  Comment files are Attachments rows linked to the comment (`ActivityId`), no separate Attachment event.
- `PUT /api/comments/{id} {body}`: author only (403), within 10 minutes of posting (422 `edit_window_passed`), sets
  EditedAt and re-syncs mentions. System events are never editable (404 as a comment).
- Watchers are added automatically: reporter and assignee on create, assignee on every (re)assignment and pick,
  everyone who comments.
- `GET /api/issues/{key}/related`: open issues (not Closed/Rejected) of the same module, same screen first, max 20.
- Pick race: when someone else already holds the issue a failed pick always answers 409 "Already picked by X", also if
  their pick has already moved it to In Progress.
- `AttachmentWriter` (file checks + storage + rows) is shared by issue uploads and comments; `FormFiles` reads multipart.
