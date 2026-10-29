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

## ADR-019 – E-mail, scheduled jobs and a jobs admin app (28-Oct-2026) – S19 part DECIDED (see ADR-029)
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

## ADR-025 – Issue list and detail UI (28-Oct-2026, S15)
- **List** `/issues`: filters live in the URL (`client, project, sprint, module, status, severity, priority` (comma lists),
  `assignee` (id or `none`), `overdue=1`, `q`, `sort`, `page`, `size`) so views can be bookmarked and shared. Saved views
  are kept per user in localStorage (filters + sort, no paging). Sorting and paging are server-side (≤ 100 per page).
  Bulk assign: select rows → assignee → `POST /api/issues/bulk-assign`; issues that could not be assigned stay selected
  and the first reason is shown.
- **Detail** `/issues/:key`: action buttons come only from `allowedActions` (labels: Reassign when assigned, "Add
  deployment" for MarkFixed from Fixed/Verified). Pick, Start, Resume, Take off hold, Verify run at once; Close asks to
  confirm; the rest open one `ActionDialog` (assignee, reason, reject type + duplicate key, environment checkboxes +
  version, screenshots). The forward step is the filled button; MarkFixed only from In Progress.
- Environment strip = latest deployment per environment. While a retest is pending the tester gets Pass / Fail on the
  deployment and the header's Verify / Reopen are hidden. Fail asks what still fails (+ paste).
- Screenshots for reopen / failed retest are posted as a comment right after the action (issue attachments are fixed
  once testing is completed; ADR-020). The Attachments block lists every file of the issue, comment files included.
- `IssueDto.deployments` added (environment, deployed by/at, note, version, retest result/by/at/comment).
- Developers see a "Testing in progress" banner and no actions while the sprint is in Testing; they can comment.
- **Timeline** (`shared/timeline`): `app-issue-timeline` (All · Comments · History, "Show older", inline edit of own
  comments within 10 minutes, mentions highlighted, relative time with the exact IST time on hover) and
  `app-comment-composer` (plain text, @ suggestions with ↑ ↓ Enter/Tab Esc, Ctrl+V / drop / clip files, Ctrl+Enter).
- A failed retest no longer repeats its comment on the Reopened status event (the RetestFailed event carries it).

## ADR-026 – Work queues and deploy notices (28-Oct-2026, S16)
- Queues show only issues of sprints that **completed testing** (in Testing developers cannot act; a closed sprint is
  read-only), so a tester's pre-assignment appears in My work once testing completes.
- `GET /api/me/work?projectId=` → `{assigned, available, needInfoAnswered, reopened, overdue}`, each `{count, items}`
  (≤ 100 items, most severe first, then due date, then oldest). Assigned = mine and awaiting a fix
  (`IssueStates.AwaitingFix`); Available = unassigned in a Pick from-status (`IssueWorkflow.FromStatuses`), empty for
  testers; Need info answered = mine in Need info with a comment by someone else after the latest change to Need info;
  Reopened = mine and Reopened; Overdue = mine, awaiting a fix, due date passed.
- `GET /api/retest-queue?projectId=&mine=` → `{count, groups[]}`: pending deployments of Fixed issues grouped by
  environment (name, type, URL, project), groups oldest waiting first, items in deployment order (≤ 500). `canRetest`
  per item comes from IssueWorkflow (Verify) for the caller; `mine=true` = issues the caller reported. Pass / Fail use the
  existing `POST /api/deployments/{id}/retest`.
- Deploy notice: `GET /api/deploy-notices/candidates?projectId=` (caller's own Fixed or Verified issues with the latest
  deployment per environment), `GET /api/deploy-notices?projectId=&size=` (recent, newest first, all users) and
  `POST /api/deploy-notices {environmentId, issueKeys[], note, version}` → 201 with the notice. Note is required
  (like the fix note). **All or nothing**: every issue must exist, belong to the environment's project, be assigned to
  the caller (admins too), be Fixed/Verified, pass IssueWorkflow MarkFixed and not already wait for a retest on that
  environment – otherwise 422 `not_deployable` listing each problem ("KEY: reason").
- Each issue gets a Deployment linked to the DeployNotices row and one Deployed activity (the same timeline entry as
  Mark fixed); Verified issues go back to Fixed (VerifiedAt cleared). `WorkflowSupport.Deployed/Fixed` are shared with
  Mark fixed. The single notification to the tester per notice comes with S18 from the DeployNotices row.

## ADR-027 – Work queue screens (28-Oct-2026, S17)
- **My work** `/my-work` (Developer, Admin): five tabs as an ARIA tablist (← → keys) with counts from `/api/me/work`,
  tab in the URL (`?tab=available|need-info|reopened|overdue`), rows = severity, key + title, module › screen, sprint,
  status, due (red when overdue); Available to pick has a one-click Pick (409 "Already picked by X" → toast + reload).
- **Retest queue** `/retest-queue` (Tester, Admin): one card per environment headed by an address bar (name, type,
  project, URL, "Open <environment>" in a new tab, oldest wait); items show fix note, version, who fixed it, the issue's
  page URL and the wait coloured by the plan's ageing buckets (< 1 day, 1–2, 3–5, > 5 days). Pass runs at once; Fail
  loads the issue and opens the S15 `ActionDialog` (what still fails + pasted screenshot). `?mine=1` = issues I reported.
- **Deploy notice** `/deploy-notice` (Developer, Admin, also linked from My work): candidates per project with a
  checkbox and an environment chip per latest deployment; selecting an issue fixes the project, choosing an environment
  drops/blocks issues already waiting there; composer (environment, required note, version) on the right, sticky;
  last 5 notices below it.

## ADR-028 – Real time and in-app notifications (29-Oct-2026, S18)
- **One place derives notifications and live updates from every save**: `ChangeFeed` (Application) runs inside
  `ChangeFeedInterceptor` (EF Core SaveChanges + transaction interceptor, Infrastructure). Before the save it reads the
  tracked changes – new/edited timeline entries, mentions, issues, sprints, notifications added by handlers – and adds
  the Notifications rows (same transaction). After the commit it publishes the SignalR events through
  `IRealtimePublisher`. Handlers do not notify; a rolled-back or failed save publishes nothing. EF's own save
  transaction is not mistaken for the handler's commit (pick uses its own transaction: events go out on its commit).
- **Rules (plan 8.3) from timeline entries**: Assigned → assignee (not during Testing: pre-assignees hear it from the
  Testing completed notification); StatusChanged → Reopened: assignee, NeedInfo / Disputed: reporter; Deployed →
  reporter "ready to retest" (one per issue; a deploy notice gives one per reporter and batch with the keys, linked to
  the sprint); Comment → reporter, assignee and watchers; Mention → mentioned user (also when a comment edit adds one).
  The actor is never notified; inactive users are skipped; per save each user gets at most one notification per issue,
  the most important (Reopened > NeedInfo > Disputed > Assigned > Fixed > Mentioned > Comment). Title says who did
  what ("Emily assigned PGERP-4 to you"), body = issue title + up to 200 characters of the comment / reason / note.
  CriticalUnassigned and Overdue are scheduled checks (S19 digest / S28).
- **History is kept**: notifications are rows, never deleted by reading. `GET /api/notifications?unread=&before=&size=`
  (newest first, ≤ 100 per page, `before` = last id, `{items, unreadCount, hasMore}`), `GET
  /api/notifications/unread-count`, `POST /api/notifications/read {ids | all, read = true}` (own rows only; `read:
  false` marks unread again).
- **Hub** `/hubs/tracker` (default sign-in policy; the browser passes the access token as `access_token` on `/hubs`
  requests). Each connection joins `user:{id}`; issue pages call `WatchIssue(key)` / `UnwatchIssue(key)`. Events:
  `IssueChanged {issueKey, projectId, sprintId, deleted}` and `SprintChanged {sprintId, projectId, status}` to every
  signed-in user (all users may read all issues), `ActivityAdded {issueKey, activityId, kind}` to the issue group,
  `NotificationCreated` (NotificationDto) and `NotificationsRead {unreadCount}` (other tabs) to the user. Events are
  hints; clients reload from the API.
- **Angular**: `RealtimeService` (@microsoft/signalr, auto-reconnect, token refreshed before it expires, re-joins issue
  groups, `reconnected$` makes screens reload), `reloadOnIssueChanges()` helper (debounced) used by the issue list,
  issue detail, My work, retest queue and deploy notice; the timeline joins its issue group and reloads the newest page
  on ActivityAdded (not while editing a comment; older pages are kept). `NotificationStore` + top-bar
  `app-notification-bell` (badge 99+, panel with All / Unread, mark one / all read, opens the issue or the sprint's
  issues, one bell nudge per live arrival, none with reduced motion) and the `/notifications` page (history by IST
  day – Today, Yesterday, 27/Oct/2026 – "Show older", mark read / unread). Type icons and rails use status / severity
  tokens. Tests use `FakeRealtime` (provided by `provideFakeAuth`).

## ADR-029 – E-mail notifications (29-Oct-2026, S19)
- **Decision on ADR-019 for now** (Akash: "use whatever feels correct and best"): no scheduler library yet. The outbox
  sender and the daily digest are .NET `BackgroundService`s in the API process (`EmailOutboxWorker`,
  `DailyDigestWorker`); the last run of each job is kept in `dbo.JobRuns` (script 005) so a missed 18:00 slot is
  caught up after a restart. Hangfire/Quartz and the `QaTracker.Jobs` MVC admin stay possible later (S19b/S28b) – the
  outbox and JobRuns tables are what they would read.
- **Queueing**: `NotificationMailer` runs inside the ChangeFeed (ADR-028), so instant mails go into `EmailOutbox` in
  the same save as their notification, and `Notifications.EmailedAt` marks them. Instant types: Assigned, Mentioned,
  Reopened, Fixed (one per deploy notice), NeedInfo, Disputed, TestingCompleted (the one summary per person),
  CriticalUnassigned. Digest types: Comment, Overdue. Nothing is queued while `Email:Enabled` is false.
- **Preferences**: `NotificationPreferences (UserId, Type, Email)`; no row = mailed (plan defaults). In-app is always
  on. `GET/PUT /api/notifications/preferences` → `{emailEnabled, digestTimeIst, email, items[{type, label, description,
  delivery: Instant|Digest, email}]}`; page `/settings/notifications` (user menu, bell panel, Notifications page).
- **Daily digest** at `Email:DigestTimeIst` (18:00) IST: per user the comment notifications not mailed yet (grouped by
  issue) and the issues assigned to them that are overdue (awaiting a fix, due date passed, testing completed). Comment
  notifications are marked mailed even when the user opted out, so switching the mail on later sends no backlog.
- **Sender**: System.Net.Mail SMTP (no extra library), batches of `BatchSize` oldest first, saved per mail; a failure
  keeps Attempts / LastError and waits 1, 5, 15, 30 min, then hourly, up to `MaxAttempts` (8). Mails are never deleted.
- **Templates**: inline-styled HTML (logo mark, card, one button, "Change e-mail settings" footer); everything
  user-written is HTML-encoded; dates 27/Oct/2026 IST; links use `Email:AppBaseUrl`.
- **Admin › E-mail** `/admin/email` (Akash, S19: "SMTP settings shall be verifiable by admin through a screen"):
  `GET /api/admin/email` shows the effective settings (password only as set / not set), outbox counts (waiting, gave
  up, sent in 24 h), mails not delivered (with "Try again" = fresh attempts) and the latest mails, and the digest's last
  run; `POST /api/admin/email/test {to}` sends straight through SMTP (also while e-mail is off) and returns the server's
  answer or 422 `smtp_failed` with its message; `POST /api/admin/email/digest` queues the summary now. Editing the
  settings in the app stays with S28; today they live in appsettings (`Email` section) + user secrets
  (`Email:Smtp:Password`).
- **Dev**: smtp4dev as a local dotnet tool – `dotnet smtp4dev --urls=http://localhost:5081 --smtpport=2525 --db=`
  (inbox at http://localhost:5081); Development settings point at localhost:2525. Integration tests queue mails but run
  no workers (`Email:Workers=false`) and use a closed SMTP port for the failure path.
- **Scrollbars** (Akash, S19): thin rounded thumbs without track or arrows everywhere (`--qa-scrollbar-thumb(-hover)`,
  dark variant), a faint light thumb on the ink sidebar (`.qa-sidebar-scroll`); Firefox gets `scrollbar-width: thin`.

## ADR-030 – Kanban board (29-Oct-2026, S20)
- `GET /api/board?projectId=&sprintId=&assigneeId=|mine=true|unassigned=true&severity=&q=` → `{projectId, columns[]}`:
  one column per status in workflow order (New, Reopened, Assigned, In Progress, Need Info, On Hold, Disputed, Fixed,
  Verified, Closed, Rejected), each `{status, count, cards}` with at most 200 cards (most severe, then due date, then
  oldest). The board is per project (400 without, 404 unknown); without a sprint it shows every sprint that is not
  closed. A column over the limit is read with its own TOP query so Closed cannot crowd out open work.
- Each card carries `moves` = the IssueWorkflow allowed actions for the caller whose target is another status
  (re-assigning in place is not a move) plus its rowVersion. The UI never repeats the rules: a drop is accepted only
  when a move leads to that column; otherwise it is refused with a toast that names what the card can do instead
  (closed sprint and "testing in progress" get their own wording).
- **UI** `/board` (all roles; filters in the URL: client, project, sprint, who = me|none|user id, severity, q,
  done = show Closed/Rejected; last project remembered per browser). Angular CDK drag-drop (part of @angular/cdk – no new
  library). While dragging, reachable columns take their status colour and say "Drop to <action>", the others fade.
  Direct actions (Pick, Start, Resume, Take off hold, Verify; Close after confirm) run at once; the rest load the issue
  (fresh version and actions) and open the S15 `ActionDialog`; cancel puts the card back. The card menu (⋮) offers the
  same moves for keyboard and touch. 409 → toast and reload. Live: reloads on IssueChanged of the project.
- `IssuesApi` workflow calls take `IssueRef` (key + rowVersion), so board cards and issues share them.

## ADR-031 – Analytics API (29-Oct-2026, S21)
- Routes: `GET /api/analytics/overview | developers | quality | clients?clientId=&projectId=&sprintId=&from=&to=` and
  `GET /api/analytics/sprints/{sprintId}`. Every signed-in user may read them. Dates are IST days (`yyyy-MM-dd`);
  default range = the last 30 days up to today; `to` before `from` or more than 366 days → 400.
- **What the range applies to**: widgets about events in time – created vs closed per day, recent activity, fixes per
  week and the fix metrics (average fix time, first-time-right, SLA), retest turnaround, severity trend, closure time.
  Current-state widgets (KPIs, status groups, open by severity / project / client, workload, ageing, hotspots, leakage,
  rejected ratio, health) show today within the client / project / sprint scope.
- **Status groups**: Open (New, Assigned, In Progress, Need Info, On Hold, Disputed, Reopened = `IssueStates.AwaitingFix`),
  Fixed, Verified, Closed, Rejected; each group lists its statuses so the UI can click through to the issue list.
- **History from the timeline**: "open a week ago" and the sprint burn-down use the latest StatusChanged activity at
  that moment (no status → New). A fix = the first StatusChanged to Fixed of an issue, credited to who made it.
  Average fix time = first fix − max(testing completed, first assignment) in working hours; first-time-right =
  1 − reopened / fixed; SLA compliance = fixed on or before the due date / fixed with a due date.
- **Working time**: `WorkingCalendar.WorkingHoursBetween` counts the clock time on working days (Sundays and holidays
  skipped, IST) with a full day = 8 working hours – the inverse of `AddWorkingHours`. Closure time per client is in
  working days; retest turnaround and fix time in working hours.
- **Sprint dashboard**: testing days = working days from start to testing completed (or now); found per testing day =
  issues created up to testing completed; burn-down from testing completed to the later of today and the latest due
  date (`open` null for days still to come; ideal from the open count at testing completed to 0 on the latest due date);
  comparison = issues per sprint of the project by severity in start order.
- **Developers**: workload columns Assigned (incl. Reopened), In Progress, Need Info, On Hold; `me` = the caller's
  open, overdue, fixed this week (since Monday IST) and average fix time (all time).
- **Quality**: leakage = issues per project by sprint phase (phases in sort order, only those used); ageing buckets as
  the retest queue (< 1 day, 1–2, 3–5, > 5 days, calendar days); hotspots = top 30 modules.
- **Client health**: Red when a critical issue is open or more than 3 are overdue, Amber when anything is overdue,
  otherwise Green. UAT vs LIVE uses the found-on environment's type (an issue without one takes the sprint default).
- Aggregation is grouped in SQL; per-row work happens in memory only on bounded sets (one sprint's history, fixes and
  retests in scope). Measured on 5 000 seeded issues: overview ≈ 250 ms, sprint ≈ 180 ms, others ≤ 60 ms (test asserts
  < 800 ms per endpoint).

## ADR-032 – Chart kit and Overview dashboard (29-Oct-2026, S22)
- **Library**: Apache ECharts 6.1 via ngx-echarts 22 (plan 9.6). Only the used charts/components are registered
  (`shared/charts/echarts-core.ts`) and loaded lazily through `provideCharts()` on chart routes, so ECharts is its own
  chunk (~190 kB transferred) and never in the initial bundle.
- **Kit** (`shared/charts`): `app-chart` (card: title, one-line subtitle, loading skeleton, error with "Try again",
  empty message, table view toggle, canvas, click → `{key, series}`), wrappers `app-chart-bar`, `-stacked-bar`
  (also 100 %), `-donut`, `-line`, `-heatmap`, `-treemap`, `-gauge`, and `app-kpi-card` (label, value, one line of
  context, link; `alertWhenAboveZero` adds a red edge and an icon – never colour alone). Options are pure functions
  (`chart-options.ts`) so they are unit-tested without a canvas.
- **Theme**: `ChartTheme` reads the CSS tokens (no hard-coded colours) and re-reads them when the `app-dark` class
  changes, so charts repaint with the theme. Marks follow the dataviz rules: bars ≤ 24 px with a 4 px rounded end,
  2 px lines, 2 px surface gaps/rings, hairline solid grid, legends for ≥ 2 series, text in text tokens, tooltips
  confined to the chart (an unconfined hidden tooltip widened the page).
- **Colour check**: the fixed severity colours fail a categorical CVD check (High vs Medium ΔE ≈ 7 normal / 1.6
  deutan). They stay (plan 10.2) but are never the only cue: bar charts label every category on the axis, stacked
  charts carry a legend and the table view. S23/S24 stacked-by-severity charts must keep labels/legend.
  Created vs closed uses primary + status-closed (validated: ΔE 33 / 28 deutan).
- **Global filter bar** `app-dashboard-filters`: Client, Project, Sprint, Period (Last 7 / 30 / 90 days, This month,
  Custom range with a Material date-range picker showing dd/MMM/yyyy via `IstDateAdapter`). Kept in the URL
  (`client, project, sprint, period, from, to`); presets become IST days for the API (`analyticsQuery`).
- **Overview** is the home page (`/home`, also `/dashboards/overview`): five KPI cards (Open with trend vs a week ago,
  Awaiting retest → retest queue, Overdue, Critical open, Closed this week), Issues by status (donut), Open by severity,
  Open by project, Created vs closed (line, per IST day) and Recent activity (20, scrolls beside the trend). Clicks open
  the issue list with the dashboard scope plus the status group / severity / project; the trend line has no
  click-through (the issue list has no date filter). Reloads on live issue changes (debounced 1.5 s).
- `formatIst` now uses fixed month names (browsers' newer locale data print "Sept").

## ADR-033 – Sprint and Developer dashboards (29-Oct-2026, S23)
- **Sprint dashboard** `/dashboards/sprint?client&project&sprint` (filter bar without Period – the sprint is the
  period). A project without a sprint opens its newest sprint. Linked from the Testing page ("Sprint dashboard"
  beside the sprint stats); the Sprints page itself is still the S29 placeholder.
  Widgets: header (phase, status, started, testing days, testing completed, issue count → list, latest due date),
  Progress (100 % bar of the status groups), Burn-down (open per day + dashed ideal), Issues found per testing day
  (columns), Issues by type, Module × severity heat map (only severities that occur), Reopened (KPI + list, most
  reopened first), Sprint comparison (stacked columns by severity, current sprint marked "(this)").
  Click-through: progress part → sprint + group statuses; heat-map cell → sprint + module + severity; comparison part →
  that sprint + severity. Verified in Chrome: the heat-map cell "1" listed exactly 1 issue.
- **Deviations from plan 9.2**: "Issues by type" is a single-hue bar, not a donut (a donut needs a categorical colour
  per type and its slices are harder to compare); "Sprint comparison" is stacked rather than grouped (totals per
  sprint stay readable); neither type nor found-per-day has click-through (the issue list has no type or date filter).
- **Developer dashboard** `/dashboards/developers` (full filter bar). My stats (Developer/Admin only – testers fix
  nothing): my open, my overdue (alert), fixed this week, my average fix time ("12.5 h"). Workload (stacked: Assigned
  incl. Reopened, In Progress, Need Info, On Hold; click → assignee + statuses, matched the list in Chrome), SLA
  compliance gauge ("2 of 2 fixes…"), Fixed per week (a line per developer), Average fix time (working hours) and
  First-time-right (%, framed as a quality signal) with the issue count in each label.
- **Categorical colours**: new tokens `--qa-chart-1…6` (light and dark steps of the dataviz reference palette),
  validated with the palette script on the app surfaces (light #ffffff, dark #0f172a): CVD ΔE ≥ 9, normal ≥ 19; three
  light hues are below 3:1 contrast, so these charts always have a legend and the table view. Only for identity
  (people, projects) – never status or severity. More than 5 developers: the rest are summed as "Others (n)" in muted
  ink, never a generated 7th hue.
- Chart kit additions: `app-chart-bar` `format` (value text in labels, tooltip and table), `max` (100 for %),
  `zeroIsEmpty` (false for metrics, where 0 h or 0 % is a real value); value labels only up to 12 bars; columns get
  room above for their labels; `app-kpi-card` accepts a formatted string; `app-dashboard-filters [showPeriod]`.

## ADR-034 – Quality and Client health dashboards (29-Oct-2026, S24)
- **Quality dashboard** `/dashboards/quality` (full filter bar). KPIs: Retest turnaround (median working hours, number
  of retests in the period), In the retest queue, Waiting over 5 days (alert), Rejected ratio (rejected of all
  issues) – the first three open the retest queue, the ratio the rejected issues.
  Widgets: Defect leakage (columns per project stacked by sprint phase, phases in sort order with `--qa-chart-1…6`,
  more than six summed as "Others"), Rejected by reason (donut, categorical tokens, click → rejected issues), Severity
  trend (stacked areas per IST week, only severities that occur), Retest turnaround by environment type (bar, hours),
  Retest queue ageing (columns, "> 5 days" in the critical colour, click → retest queue), Module hotspots (treemap,
  click → issue list for the module – matched 6 of 6 in Chrome).
- **Client health** `/dashboards/clients` (full filter bar). Client cards sorted Red, Amber, Green, then by name: open,
  critical and overdue (each links to the issue list for that client), last sprint (→ sprint dashboard) and the
  health badge. `app-health-badge` (shared/ui, in /dev/ui) shows an icon and a word – Healthy / At risk / Critical –
  so colour is never the only cue; red and amber cards also get a coloured left edge.
  Charts: Open by client & severity (stacked, click → that client's open issues of the severity), Average closure time
  (working days, issue count in the label), UAT vs LIVE (stacked columns per project; "Other" and "Not set" only when
  they occur).
- **Deviations from plan 9.4 / 9.5**: UAT vs LIVE is stacked rather than grouped (as the sprint comparison, ADR-033);
  leakage, UAT vs LIVE, turnaround and the trend have no click-through (the issue list has no phase, environment or
  date filter). Retest turnaround shows "–" when nothing was retested and the rejected ratio "–" with no issues.
- Chart kit: `app-chart-line [stacked]` stacks the series as filled areas (75 % fill).
