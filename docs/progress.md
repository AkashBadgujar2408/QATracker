# QA Tracker – progress log

Plan: 32 sessions (S01–S32), ~2 h each. Milestones: M1 after S11 · M2 after S17 (go live internally) · M3 S20 · M4 S24 · M5 S27 · M6 S32.

| Session | Title | Status |
| --- | --- | --- |
| S01 | Solution & workspace scaffold | ✅ Done (27-Oct-2026) |
| S02 | App shell & shared UI primitives | ✅ Done (27-Oct-2026) |
| S03 | Domain entities & database (database-first) | ✅ Done (27-Oct-2026) |
| S04 | Authentication API (UserName login, plain-text passwords) | ✅ Done (27-Oct-2026) |
| S05 | Authentication UI & users admin | ✅ Done (27-Oct-2026) |
| S06 | Masters API | ✅ Done (27-Oct-2026) |
| S07 | Masters UI + entity-picker | ✅ Done (27-Oct-2026) |
| S08 | Testing Sprint API | ✅ Done (28-Oct-2026) |
| S09 | Issue API – create, edit, list | ✅ Done (28-Oct-2026) |
| S10 | Attachments | ✅ Done (28-Oct-2026) |
| S11 | Testing workspace UI (M1) | ✅ Done (28-Oct-2026) |
| S12 | IssueWorkflow domain | ✅ Done (28-Oct-2026) |
| S13 | Workflow API | ✅ Done (28-Oct-2026) |
| S14 | Timeline & comments API | ✅ Done (28-Oct-2026) |
| S15 | Issue list & detail UI | ✅ Done (28-Oct-2026) |
| S16 | My work, retest queue & deploy notice API | ✅ Done (28-Oct-2026) |
| S17 | My work, retest queue & deploy notice UI (M2) | ✅ Done (28-Oct-2026) |
| S18 | SignalR & in-app notifications | ✅ Done (29-Oct-2026) |
| S19 | Email notifications | ✅ Done (29-Oct-2026) |
| S20 | Kanban board (M3) | ✅ Done (29-Oct-2026) |
| S21 | Analytics API | ✅ Done (29-Oct-2026) |
| S22 | Chart components & Overview dashboard | ✅ Done (29-Oct-2026) |
| S23 | Sprint & Developer dashboards | ✅ Done (29-Oct-2026) |
| S24 | Quality & Client health dashboards (M4) | ✅ Done (29-Oct-2026) |
| S25 | Sprint report export | ✅ Done (29-Oct-2026) |
| S26 | Excel import API | ✅ Done (29-Oct-2026) |
| S27 | Import UI | ✅ Done (29-Oct-2026), check moved to S27b |
| S27b | Import end-to-end check (M5) | ✅ Done (30-Oct-2026) |
| S28 | SLA, overdue & settings | ✅ Done (30-Oct-2026) |
| S29 | Sprint close & carry forward | ⏭ Next |
| S30 | Security & integration test pass | |
| S31 | UX polish & performance | |
| S32 | Deployment & handover (M6) | |

---

## S01 – Solution & workspace scaffold   (27-Oct-2026)

**Done:**
- `docs/plan.md` generated from QA_Tracker_Build_Plan.docx; `CLAUDE.md`, `docs/progress.md`, `docs/decisions.md`.
- `QaTracker.sln`: Domain, Application, Infrastructure, Api + Domain.Tests, Application.Tests, Api.IntegrationTests;
  references point inwards. `Directory.Build.props` (net10.0, nullable, warnings as errors, analyzers, code style in build),
  `Directory.Packages.props` (central versions), `global.json`, `.editorconfig`, `.gitignore`, `dotnet-tools.json` (dotnet-ef).
- API: Serilog (console + daily rolling file `logs/`, request logging with CorrelationId), ProblemDetails,
  OpenAPI + Scalar (`/scalar`, dev only), CORS for `http://localhost:4200` (dev), HSTS outside dev, `/health`.
- Database-first setup: connection string in appsettings.json, `database/scripts/000_CreateDatabase.sql`,
  `database/scaffold-command.txt` (Scaffold-DbContext + dotnet ef equivalent), EF Core Tools/Design packages.
- `qa-tracker-web`: Angular 22 (standalone, zoneless, strict + strictTemplates), Tailwind 4, PrimeNG with `QaPreset`
  (indigo/slate, 6 px radius), Inter font, design tokens (10.2) as CSS variables for light/dark, ThemeService
  (`app-dark` class, remembers choice, defaults to OS preference), themed page with dark-mode toggle.
  ESLint (angular-eslint; OnPush, standalone, no-explicit-any as errors) + Prettier.
- Tests: layer dependency tests (Domain, Application), `/health` integration test, Angular app/theme tests.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 3/3 · `ng build` clean · `ng lint` clean · `ng test` 2/2 ·
`/health` 200, `/openapi/v1.json` 200, `/scalar` served, CORS header returned for localhost:4200.

**Deviations from plan:** database-first + connection string in appsettings.json (ADR-001/002); SQL Express instead of
Docker (ADR-003); xUnit v3 on MTP (ADR-004); bundle budget 1 MB (ADR-005); Pkcs transitive pin (ADR-006).
Solution file kept as `.sln` (plan 11.2) rather than the new `.slnx` default.

**Carry-over / known gaps:**
- Database not created yet – S03 runs `000_CreateDatabase.sql` + schema scripts and scaffolds.
- Integration-test DB strategy without Docker – decide in S04 (ADR-003 proposal).
- FluentValidation, Mapperly, ClosedXML, QuestPDF, HtmlSanitizer, ngx-echarts, TipTap not added yet – added in the
  session that first needs them (versions listed below are the ones checked today).
- Visual check of the themed page in light/dark to be done by Akash (`npm start`).

**Libraries added (licence) – pinned versions:**
| Package | Version | Licence |
| --- | --- | --- |
| .NET SDK | 10.0.401 | MIT |
| Microsoft.AspNetCore.OpenApi | 10.0.14 | MIT |
| Scalar.AspNetCore | 2.18.3 | MIT |
| Serilog.AspNetCore | 10.0.0 | Apache-2.0 |
| Microsoft.EntityFrameworkCore.SqlServer / Design / Tools, dotnet-ef | 10.0.14 | MIT |
| System.Security.Cryptography.Pkcs (transitive pin) | 10.0.14 | MIT |
| xunit.v3 | 4.0.2 | Apache-2.0 |
| Shouldly | 4.3.0 | BSD-3-Clause |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | MIT |
| Angular (core/common/router/forms/…) / CLI | 22.2.3 / 22.2.4 | MIT |
| @angular/cdk | 22.2.4 | MIT |
| ~~primeng~~ | ~~22.1.4~~ | **Not MIT – PrimeUI commercial licence; removed in S02 (ADR-007)** |
| ~~@primeuix/themes~~ | ~~3.1.0~~ | **PrimeUI licence; removed in S02** |
| tailwindcss / @tailwindcss/postcss | 4.3.3 | MIT |
| @fontsource-variable/inter | 5.3.0 | OFL-1.1 |
| angular-eslint / eslint / typescript-eslint | 22.6.0 / 10.13.0 / 8.71.0 | MIT |
| prettier | 3.9.10 | MIT |
| typescript | 6.0.3 | Apache-2.0 |
| vitest | 4.1.12 | MIT |

Checked for later sessions: FluentValidation 12.2.0 (Apache-2.0), ngx-echarts 22.0.0 + echarts 6.1.0 (MIT / Apache-2.0).

**Next session:** S02 – App shell & shared UI primitives.

---

## S02 – App shell & shared UI primitives   (27-Oct-2026)

**Done:**
- **UI kit switched to Angular Material 22 + CDK** (PrimeNG is no longer MIT – ADR-007); M3 theme mapped onto our
  tokens (colours, radii, button shape), density -2. Lucide icons via @ng-icons (ADR-008).
- Shell (`core/layout`): collapsible sidebar (remembered, tooltips when collapsed, Dashboards group, active state with
  aria-current), top bar (search placeholder, theme toggle, bell placeholder, user menu placeholder), skip link,
  breadcrumb bar fed by `BreadcrumbService` (route `data.breadcrumb`, pages can `set()` a trail),
  `PageTitleStrategy` ("Issues · QA Tracker"), `LayoutService`.
- Shared UI (`shared/ui`): page-header, empty-state (empty / no-results / error / info), status-pill, severity-badge,
  priority-icon, user-avatar (initials, stable token colour or Users.AvatarColor), skeleton-lines, skeleton-table,
  breadcrumb, confirm-dialog. Services: `ToastService` (success 3 s), `ConfirmService.ask()` (danger → focus Cancel).
- Models: IssueStatus / Severity / Priority with labels + colour tokens; Role.
- Routes: every nav item opens a placeholder page naming the session that builds it; not-found page;
  hidden living style guide at **/dev/ui**.
- New tokens: border-strong, avatar palette (8) + avatar text, danger contrast, sidebar/topbar sizes.

**Verified:** `ng build` clean (initial 494 kB) · `ng test` 31/31 · `ng lint` clean · no hex colours in src/app ·
/dev/ui checked in Chrome in light and dark (tokens, badges, avatars, buttons, dialog, toast).

**Deviations from plan:** Angular Material instead of PrimeNG (ADR-007); Lucide icons (ADR-008).
Project switcher in the top bar (plan 10.3) not in the S02 card – deferred to S07 (needs masters API).

**Carry-over / known gaps:**
- Role-based nav filtering waits for auth (S05) – `NavItem.roles` is already defined.
- Global search and "/" shortcut → S15; bell → S18; user menu actions → S05.
- ADR-005 budget raise is no longer needed (initial bundle back under 500 kB) – keep 1 MB budget anyway.

**Libraries added (licence):** @angular/material 22.2.4 (MIT) · @ng-icons/core 36.1.0 (MIT) · @ng-icons/lucide 36.1.0 (ISC).
**Libraries removed:** primeng, @primeuix/themes, primeicons (PrimeUI commercial licence).

**Next session:** S03 – Domain entities & database (database-first).

---

## S03 – Domain entities & database (database-first)   (27-Oct-2026)

**Done:**
- `database/scripts/001_InitialSchema.sql`: all 24 tables of plan section 12 (ADR-010 naming), FKs, CHECK constraints for
  every enum column and the project key, unique keys, plan indexes, filtered unique index
  `UX_TestingSprints_OneTestingPerProject`, rowversion on Issues, TestingSprints, masters and Users, issue-child cascades.
- `database/apply-scripts.ps1`: creates the DB if missing and applies each script once (`meta.SchemaVersions`).
- Scaffold (`Scaffold-DbContext`, command in `database/scaffold-command.txt`, now with `--schema dbo`):
  24 POCO entities in `QaTracker.Domain/Entities`, `QaTrackerDbContext` in Infrastructure/Persistence.
  Customised `CodeTemplates/EFCore/EntityType.t4` generates enum-typed properties (13 enums in `Domain/Enums`);
  `QaTrackerDbContext.Conventions.cs` stores all domain enums as strings.
- `IClock` (Application) + `SystemClock`; `AddInfrastructure()` (DbContext, clock, seed options validated on start).
- `DatabaseSeeder` (idempotent): 5 sprint phases, SLA defaults (8/24/56/120 working hours), first admin
  (UserName `admin`, password from user-secrets `Seed:AdminPassword`, must change on first login). Runs at API start
  when `Database:SeedOnStartup` = true (Development only).
- Mid-session decisions from Akash: **login by UserName** (ADR-011), **plain-text passwords** (ADR-009, risk recorded).
- Tests: `TestDatabase` fixture (throw-away DB from the scripts), model tests (24 tables, 17 enum columns as strings,
  filtered index), seeder runs twice without duplicates, enum names accepted by CHECK constraints; health test no longer
  seeds the dev DB.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 9/9 · cascade delete of an issue with deployment/activity/
attachment/mention/watcher/link/notification leaves no children (manual SQL, rolled back) · second Testing sprint and
lower-case project key rejected · API start seeded the local dev DB (admin, 5 phases, 4 SLA rows) · no test DBs left behind.

**Deviations from plan:** database-first instead of migrations (ADR-001); custom auth tables instead of Identity, plain-text
passwords (ADR-009); naming/extra columns (ADR-010); UserName login (ADR-011).

**Carry-over / known gaps:**
- ~~Due-date rule for severity `NotAssessed`~~ – decided: default SLA 30 working days, admin-configurable (ADR-016).
- Issue delete handler must remove inbound `IssueLinks` (ToIssueId) first – S09.
- Local dev admin password is in user-secrets: `dotnet user-secrets list --project src/QaTracker.Api`
  (or read `dbo.Users.Password`).
- `Microsoft.EntityFrameworkCore.Templates` dotnet-new template installed on this machine (needed only to regenerate
  the T4 files, not to scaffold).

**Libraries added (licence):** none new. Infrastructure now references the ASP.NET Core shared framework (MIT).

**Next session:** S04 – Authentication API (UserName + password, roles, JWT + refresh cookie, users admin endpoints).

---

## S04 – Authentication API   (27-Oct-2026)

**Done:**
- `002_RefreshTokens.sql` (hashed, rotating refresh tokens) – applied and re-scaffolded.
- Application: `Result<T>`/`Error` (validation/401/403/404/409/422), `IApplicationDbContext`, `ICurrentUser`, `ITokenService`,
  `AuthOptions`, `PasswordRules` (policy, constant-time compare, temporary password generator), `AddApplication()`.
- Auth use cases: login (UserName, lockout), refresh (rotation + replay detection), logout, me, change-password.
- Users admin: list (search/role/active), create (temporary password returned once), update (rowversion → 409, role change
  ends sessions), reset password (unlocks, ends sessions), activate/deactivate (no self / last-admin rules).
- API: JWT bearer with per-request stamp/active check, policies SignedIn / Authenticated (default + fallback) /
  TesterOrAdmin / DeveloperOrAdmin / AdminOnly, login rate limiter, ProblemDetails mapping, enums as strings in JSON,
  OpenAPI bearer scheme for Scalar, Serilog no longer freezes the static logger (several hosts in tests).
- Tests: `ApiFactory` (assembly fixture, throw-away DB, https client with cookies) + 21 auth/users integration tests.
- `.gitattributes` (LF in working tree) – scripted edits failed on CRLF files.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 32/32 · dev API: admin login returns a token, `/api/users` without
token → 401, OpenAPI document contains the Bearer scheme.

**Deviations from plan:** custom auth instead of ASP.NET Core Identity (ADR-009/013); one file per use case (ADR-012).

**Carry-over / known gaps:**
- Dev signing key is in user-secrets (`Auth:SigningKey`); production must set `Auth__SigningKey` (S32).
- Login lockout + rate limit messages are English only.

**Libraries added (licence):** Microsoft.AspNetCore.Authentication.JwtBearer 10.0.14 (MIT) · FluentValidation 12.2.0 +
FluentValidation.DependencyInjectionExtensions 12.2.0 (Apache-2.0) · Microsoft.EntityFrameworkCore 10.0.14 in Application (MIT).

**Next session:** S05 – Authentication UI & users admin.

---

## S05 – Authentication UI & users admin   (27-Oct-2026)

**Done:**
- API: "Remember me" – `003_RefreshTokenPersistence.sql` (IsPersistent carried through rotation and password change);
  session cookie when off. 2 new integration tests.
- Angular: dev proxy (`proxy.conf.json`, /api → :5029, same-origin cookie); `AuthService` (signals, in-memory access token,
  shared refresh, session restore on app start); `authInterceptor` (bearer + one refresh-and-retry on 401);
  `errorToastInterceptor` (offline/403/429/5xx; forms opt out with `SILENT_ERRORS`); guards (auth, role from route data,
  signed-in, guest, forced password change); `*appHasRole`; `istDate` pipe; shared form error helpers.
- Pages: Sign in (user name, show/hide password, remember me, safe returnUrl), Change password (forced + voluntary),
  Admin › Users (search/role/status toolbar, table, create/edit dialog, one-time temporary password dialog with copy,
  reset password, activate/deactivate with confirmation; loading/empty/error states).
- Shell: sidebar filtered by role, real user menu (change password, sign out).
- Design pass (frontend-design skill, ADR-014): app logo + favicon, ink sidebar with animated indented submenu, split
  sign-in screen, larger type scale, dd/MMM/yyyy dates, table polish, icon alignment fix.

**Verified:** `ng build` clean · `ng test` 58/58 · `ng lint` clean · `dotnet test` 34/34 · in Chrome against the real API:
admin first sign-in forced a password change; created a tester and a developer in Admin › Users (temporary passwords
shown once); signed in as each (forced change) – Tester sees Testing/Retest queue/Masters, Developer sees My work,
neither sees Users; Developer opening /admin/users goes Home.

**Deviations from plan:** ADR-014 (type scale, date format, visual identity).

**Carry-over / known gaps:**
- Dev accounts on the local DB: `admin`, `akash.tester`, `nitin.dev` – passwords are readable in `dbo.Users`
  (ADR-009); the admin password is also in user-secrets `Seed:AdminPassword`.
- Avatar colour is API-only (no picker in the UI yet).
- Once, after a password change the Chrome automation tab stopped responding; not reproducible manually or in tests.

**Libraries added (licence):** none (Angular Material/CDK already present).

**Next session:** S06 – Masters API.

---

## S06 – Masters API   (27-Oct-2026)

**Done:**
- Clients: list (search, include inactive), get, create (quick-add = name only), update (rowversion), activate/deactivate.
- Projects: list per client, get (with module/environment/member counts, hasIssues), create (key validated, normalised,
  globally unique; name unique per client; client must be active), update (key locked once issues exist), activate/deactivate.
- Modules and environments (type UAT/LIVE/Other, http(s) URL): list per project, create, update, activate/deactivate;
  names unique per project.
- Members: list, replace set (active users only).
- Lookups: cascade (clients → projects → modules/environments/members, active only), similar names, project key suggestion.
- Shared `ExpectRowVersion` / `TrySaveAsync` (concurrency → 409, unique-index race → 409) / `ValidRowVersion`.
- Tests: 17 unit tests (similarity, key suggestion/validation) + 10 integration tests (hierarchy build, developer 403 on writes
  but can read, uniqueness per scope, key rules, 409 on stale edit, inactive client rules, URL validation, similar names,
  key suggestion, member validation).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 61/61 · dev API: all 16 masters routes in OpenAPI (Scalar),
cascade returns data with a token, similar names without parent → 400.

**Deviations from plan:** route refinements and member replace-set (ADR-015); grouped use-case files for small masters (ADR-012).

**Carry-over / known gaps:** none for the API. S07 builds the Masters page, the entity-picker and quick-add dialogs on top.

**Libraries added (licence):** none.

**Next session:** S07 – Masters UI + entity-picker.

---

## S07 – Masters UI + entity-picker   (28-Oct-2026)

**Done:**
- `shared/data/masters.api.ts`: typed client for all masters routes + lookups (cascade, similar, project-key, users).
- API: `GET /api/lookups/users?role=` – active users for member/assignee pickers (testers cannot read the admin-only
  `/api/users`). 1 integration test.
- Shared `app-entity-picker` (mat-autocomplete): search, arrow keys/Enter/Esc, "+ Add 'typed text' as new …" option,
  `[(value)]` or form control (ControlValueAccessor via NgControl, required error), exact typed name selects on blur,
  unknown text reverts, emits `created`. Added to /dev/ui as a live Client → Project → Module → Environment cascade.
- Shared `MasterDialog` + `MasterDialogs` service: quick-add for client, project (key auto-suggested until edited,
  upper-cased), module, environment (type + URL); edit for module/environment. Similar names flagged as you type;
  an exact duplicate blocks saving and offers "Use it"; similar ones offer "Use this". `MasterDialogs.quickAdd(kind, parent)`
  builds the picker callback.
- Masters page (`/masters`, Tester/Admin): tree Clients → Projects → Modules/Environments/Members with counts,
  find box, show-inactive toggle, selection in the URL (`?client=&project=&tab=`), client panel (details form, projects
  list, new project, activate/deactivate with confirmation), project panel (name, key – locked once issues exist,
  LIVE verification flag, lazy tabs for modules / environments / members). Loading, empty and error states everywhere.
- `plural` pipe ("1 module", "3 modules").
- Tests: entity-picker (6), master dialog (3), masters page (3), plural pipe (1).

**Verified:** `dotnet build` 0 warnings · `dotnet test` (integration 43/43) · `ng build` clean · `ng test` 71/71 ·
`ng lint` clean · in Chrome as a tester: built Pardi Gears › ERP (key PGERP suggested) › Sales Return › UAT 1 entirely
from the /dev/ui pickers, keyboard only (type, Enter on "Add …", Enter in the dialog); typing "sales returns" flagged
"Sales Return" as similar and "Use this" selected it; Masters tree, client panel, project panel and Members tab show the result.

**Deviations from plan:** extra `GET /api/lookups/users` endpoint (needed for members; same read rules as other lookups).

**Carry-over / known gaps:**
- Picker list filtering is substring-only; near-duplicates (typos, plurals) are caught by the quick-add dialog instead.
- Chrome automation: screenshots time out for a while after in-page URL changes (page itself stays responsive).
- Tree is a set of buttons with aria-expanded/aria-current, not a full ARIA tree (no arrow-key navigation in the tree).

**Libraries added (licence):** none (Angular Material autocomplete/tabs/slide-toggle already present).

**Next session:** S08 – Testing Sprint API.

---

## S08 – Testing Sprint API   (28-Oct-2026)

**Done:**
- Domain: `RuleCheck`, `SprintRules` (start, complete testing, reopen testing, close, issue changes, finished statuses),
  `WorkingCalendar` (SLA due dates in IST, Sundays/holidays skipped), `SprintNames` (suggestion with rounds).
- Application `Features/Sprints`: list per project, get (with history), suggested name, completion summary, start,
  complete testing (due dates, activities, notifications, event), admin reopen testing (note required); `PhaseLookupHandler`.
- API `SprintsEndpoints` + `GET /api/lookups/phases`; ADR-017.
- Tests: 47 domain tests for the sprint rules matrix (every role × status), calendar and names; 6 integration tests
  (second Testing sprint 422, developer 403 but can read, completion side effects, stale rowversion 409, reopen rules,
  phase/environment validation). `ApiFactory` now seeds reference data (phases, SLA) into the throw-away DB.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 127/127 · dev API as the tester: phases lookup, suggested name
"Sprint 1 – UAT", started it on PGERP (201), a second start → 422 "'Sprint 1 – UAT' is still in testing…".

**Deviations from plan:** extra suggested-name and completion-summary routes; close and carry-forward stay with S29
(`SprintRules.CanClose` is ready).

**Carry-over / known gaps:**
- Malformed JSON bodies (e.g. a request not sent as UTF-8) return 500 instead of 400 – the exception handler should map
  `BadHttpRequestException` to its status code.
- Hand-edited due dates are recalculated if an admin reopens testing and it is completed again (ADR-017, S13).
- Local dev DB now has "Sprint 1 – UAT" in Testing on PGERP.

**Libraries added (licence):** none.

**Next session:** S09 – Issue API – create, edit, list.

---

## S09 – Issue API – create, edit, list   (28-Oct-2026)

**Done:**
- Domain `IssueStates` (awaiting-fix statuses, overdue).
- Application `Features/Issues`: create (sprint must be in Testing, environment defaults from the sprint, Created +
  Assigned activities), update (If-Match rowversion, FieldChanged/Assigned activities, New ⇄ Assigned), delete (inbound
  links and duplicate references first), get by key (case-insensitive, with permissions), list (all plan filters,
  paging, sorting), screen suggestions. `IIssueNumbering` (sp_getapplock) and `IRichTextSanitizer` (HtmlSanitizer).
- API `IssuesEndpoints` (+ ETag headers, 428 without If-Match); ADR-018.
- Tests: 8 integration tests – numbering + activities, 20 parallel creates by two testers get 1…20, required
  severity / sanitising, If-Match/409/field-change log, gating after Testing Completed (tester 422, developer 403),
  delete with inbound link, list filters/sort/paging/overdue, screen suggestions.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 135/135 (domain 60, application 18, integration 57) ·
dev API as the tester: created PGERP-1 and PGERP-2 in "Sprint 1 – UAT" (environment UAT 1 filled from the sprint,
`<script>` removed from steps), `GET /api/issues?projectId=1&severity=High` returns PGERP-1, screens suggest
"Sales Return Entry".

**Deviations from plan:** `permissions` on the issue until `allowedActions` (S12/S13); update uses If-Match as planned,
create/delete need no version.

**Carry-over / known gaps:**
- ~~Issue delete handler must remove inbound IssueLinks first~~ – done.
- Attachment files on disk are not removed on delete yet (no attachments until S10 – handle there).
- Status sort is alphabetical; lifecycle order can come with the issue list UI (S15).
- Malformed JSON → 500 (from S08) still open.
- New requirement recorded as **ADR-019 (proposed)**: SMTP mails, daily 18:00 digest, background jobs (Hangfire),
  MVC jobs admin without delete, configurable alerts, one database. Needs Akash's confirmation before S19.

**Libraries added (licence):** HtmlSanitizer 9.2.1039 (MIT; brings AngleSharp + AngleSharp.Css, MIT).

**Next session:** S10 – Attachments.

---

## S10 – Attachments   (28-Oct-2026)

**Done:**
- API: `IFileStorage` (local folder, `Storage:Root`), `FileInspector` (magic bytes, Office zip check, UTF-8 text, 10 MB,
  clean names), `IThumbnailGenerator` (SkiaSharp WebP 320 px, 50 MP cap), `IAttachmentUrlSigner` (HMAC signed URLs, 1 h).
  Endpoints: list / upload (multipart, 1–5 files) per issue, download + thumbnail (bearer or signed URL; 403 for a bad
  signature, 401 without credentials), remove. Upload/remove write Attachment activities; issue delete removes files.
- DB: `004_IssueDeleteIndexes.sql` (applied locally, re-scaffolded) – see ADR-020.
- Angular: `AttachmentsApi`, `fileSize` pipe, shared `app-paste-dropzone` (Ctrl+V incl. page-wide capture, drag-drop,
  picker, client-side type/size checks, previews, remove, "screenshot-yyyy-MM-dd-HHmmss.png" names),
  `app-attachment-gallery` + lightbox (← → Esc, download), `app-issue-attachments` (upload at once with progress, API
  errors per upload, remove with confirmation). Both in /dev/ui.
- Tests: 12 FileInspector unit tests; 5 attachment integration tests (signed thumbnail via a plain browser client,
  tampered/other-id signature 403, no credentials 401, deactivated user 403, content/role/sprint checks, file clean-up
  on remove and issue delete); 9 Angular tests (dropzone, issue attachments, file sizes).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 154/154 (one intermittent failure before 004 – then 7 clean runs before and 1 after adding the indexes) ·
`ng build` clean · `ng test` 80/80 · `ng lint` clean · in Chrome (/dev/ui, tester): a pasted screenshot uploaded to
PGERP-1 and showed its server thumbnail; lightbox opens it full size; from the page, the signed thumbnail URL → 200
image/webp, tampered signature → 403 `attachment_forbidden`, plain URL without token → 401. Files landed in
`src/QaTracker.Api/App_Data/attachments/2026/10/` (git-ignored).

**Deviations from plan:** signed URLs (so `<img>` works without a bearer header); attachment removal endpoint; TXT also
accepts `.log`.

**Carry-over / known gaps:**
- One intermittent failure of `Delete_RemovesTheIssueAndInboundLinks` in a full parallel run before 004 (error output
  not captured); not reproduced in 8 runs since (7 before 004, 1 after). Most likely the unindexed FK scans fixed by 004 – watch for it.
- Lightbox fix: Material dialog hosts are `display: contents`, so dialog components style an inner wrapper.
- Comment attachments and attachments on workflow actions (reopen with screenshot) – S14/S13.
- **Exports (Akash's request, 28-Oct):** confirmed planned in S25 – per-sprint Excel (ClosedXML) and PDF (QuestPDF)
  report; issue list Excel export. To add to S25's scope: other testing documents (e.g. single-issue defect report PDF,
  retest/verification summary) – list to agree before S25.
- ADR-019 (SMTP, daily 18:00 digest, background jobs, MVC jobs admin) still awaiting confirmation before S19.

**Libraries added (licence):** SkiaSharp 4.154.0 + SkiaSharp.NativeAssets.Linux.NoDependencies 4.154.0 (MIT).

**Next session:** S11 – Testing workspace UI.

---

## S11 – Testing workspace UI   (28-Oct-2026)

**Done:**
- `/testing` (Tester, Admin): context bar with Client ▸ Project pickers (quick-add) and Sprint select ("+ Start new
  sprint" when none is in Testing), stat chips (Day N, total, today, by severity), "Mark testing completed" with the
  completion summary dialog; breadcrumb Testing ▸ Client ▸ Project ▸ Sprint; selection remembered per user; deep link
  `?client=&project=&sprint=`.
- Quick issue form: module/environment pickers with quick-add, screen suggestions, type, severity/priority toggles,
  title, rich-text steps/expected/actual/impact, assignee (developers/admins of the project), page URL, attachments
  (Ctrl+V anywhere, uploaded right after create; in edit mode at once). Ctrl+Enter = Save & add another (keeps module,
  screen, type, environment), N = new issue, 409 → "Load latest version".
- Right panel: this sprint's issues newest first, group by module, edit / clone / delete (with confirmation).
- Read-only once testing is completed (banner + "Start new sprint").
- Shared: `app-rich-text-editor` (in /dev/ui), `SprintsApi`, `IssuesApi`, `ISSUE_TYPES`; `EntityPicker.focus()`, Enter no
  longer submits forms from a picker. ADR-021.
- Tests: 9 new (stats, blank HTML, page states incl. read-only after completion, form keeps context after save, no
  request for an incomplete issue).

**Verified:** `ng build` clean · `ng test` 89/89 · `ng lint` clean · `dotnet build`/`dotnet test` (unchanged backend) ·
in Chrome as a local test tester: N → module/screen/severity/priority/title/steps/actual by keyboard, pasted screenshot,
Ctrl+Enter → PGERP-3 created with its attachment (201), module/screen/type kept; group by module; edit PGERP-2; deep link;
completion summary dialog opened and cancelled (local "Sprint 1 – UAT" left in Testing).

**Deviations from plan:** 60/40 split instead of 40/60 and the complete button in the context bar (Akash, ADR-021).

**Carry-over / known gaps:**
- Day counter / stats come from the first 200 issues of the sprint (enough for now; server-side counts with S21).
- Developers see a sprint's issues read-only in the Issue list (S15); the Testing route stays tester/admin only.
- Local dev DB: test account `ui.tester` (role Tester, name "John Tester") added for browser checks; deactivate it when not
  needed. PGERP-3 was created during the check.
- `execCommand` is deprecated but supported by all browsers; replace if it ever goes away.

**Libraries added (licence):** none.

**Next session:** S12 – IssueWorkflow domain.

---

## S12 – IssueWorkflow domain   (28-Oct-2026)

**Done:**
- `IssueWorkflow` (Domain/Rules): `IssueAction`, `ActionInput`, `WorkflowContext`, `WorkflowTransition`; `Check`,
  `Transition`, `AllowedActions`, `TargetOf`, `InputsOf`. ADR-022.
- `IssueDto.allowedActions` (action, target status, required inputs) on GET/POST/PUT issue, computed with the project's
  "Require LIVE verification" and passed LIVE retests; Angular `Issue.allowedActions` type for S15.
- Tests: 27 domain tests – one exhaustive matrix of 4 158 cases (14 actions × 11 statuses × 3 sprint states × 3 roles ×
  3 assignee situations) checked against plan 7.2 written out independently, AllowedActions = Check, targets and inputs
  per action, denial codes/messages, LIVE-verification rule; 1 integration test (allowedActions before/after Testing
  Completed for tester and developer).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 182/182 (domain 87).

**Deviations from plan:** no coverage tool is set up (would be a new library); the matrix enumerates every input
combination instead. Refinements listed in ADR-022 (assignee rules, reassign in any open status).

**Carry-over / known gaps:** none.

**Libraries added (licence):** none.

**Next session:** S13 – Workflow API.

---

## S13 – Workflow API   (28-Oct-2026)

**Done:**
- Application `Features/Workflow`: assign, atomic pick, transition, mark fixed (deployments), retest, bulk assign;
  `WorkflowSupport` (load, IssueWorkflow check, activities, save with rowversion, reload with allowedActions).
- API `WorkflowEndpoints`; shared `IfMatch` helper (also used by `PUT /api/issues/{key}`). ADR-023.
- Tests: 7 integration tests – developer blocked during Testing while the tester pre-assigns, pick race (6 developers,
  one wins, five get "Already picked by …"), the full loop pick → need info (comment + mention) → resume → fixed (UAT)
  → failed retest (Reopened, count 1) → start → fixed → passed retest → close → regression reopen (count 2) with the
  activity trail, "Require LIVE verification" (Verified → Fixed on LIVE → close), dispute → reject as duplicate (link),
  428/409/403/not-assignee/validation, bulk assign with per-issue failures.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 189/189 · dev API: the six workflow routes in the OpenAPI document
(Scalar), PGERP-3 as the test tester → allowedActions [Assign] while in Testing, transition without If-Match → 428.

**Deviations from plan:** If-Match on assign/transition/fixed (plan only says "rowversion"); retest passes the issue to
Verified only when no other retest of it is pending; a failed retest fails the other pending retests.

**Carry-over / known gaps:**
- Notifications for workflow events (S18/S19); watchers auto-add (S14).
- Developer actions on a Fixed issue are Assign and MarkFixed (add a deployment) – the UI should label MarkFixed from
  Fixed/Verified as "Add deployment" (S15).
- Malformed JSON → 500 (from S08) still open.
- Angular API client for workflow actions comes with the issue detail UI (S15).

**Libraries added (licence):** none.

**Next session:** S14 – Timeline & comments API.

---

## S14 – Timeline & comments API   (28-Oct-2026)

**Done:**
- Application `Features/Timeline`: activities page (filter, backwards paging), comment create (files, mentions,
  watcher) and edit (10-minute window, mentions re-synced), related issues, `TimelineReader` (names, summaries,
  mentions, comment files), `TimelineSummary`, `MentionParser`, `Watchers`; `AttachmentWriter` extracted from the
  upload handler. Watchers auto-added on create / assign / pick / bulk assign / assignee edit.
- API `TimelineEndpoints`, `FormFiles` helper (also used by attachment upload). ADR-024.
- Fixed: a pick that loses the race after the winner's pick moved the issue to In Progress answered 422; it now answers
  409 "Already picked by X" (found by the S13 race test in a full parallel run).
- Tests: 15 unit tests (mentions, edit window, summaries); 5 integration tests (order with user names, mentions, comment
  file download, Comments/History filters, paging backwards, developer comments during Testing + watchers, edit rules
  incl. other author 403 / after 10 minutes 422 / system event 404, related issues same screen first).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 211/211 (twice).

**Deviations from plan:** comments are plain text with @mentions (no rich text); timeline paging uses `before` + `size`
instead of page numbers so new entries do not shift pages.

**Carry-over / known gaps:** mention / comment notifications (S18, digest S19); real-time timeline (SignalR, S18).

**Libraries added (licence):** none.

**Next session:** S15 – Issue list & detail UI.

---

## S15 – Issue list & detail UI   (28-Oct-2026)

**Done:**
- `/issues`: filter bar (client, project, sprint, module, search, status, severity, priority, assignee incl.
  unassigned, overdue), mat-table with server sort/paging, row → detail, saved views (local), bulk assign. Filters in the
  URL (`issue-filters.ts`).
- `/issues/:key`: header (status, severity, priority, reopen count, assignee, due/overdue, sprint), developer
  "Testing in progress" banner, actions from allowedActions + `ActionDialog`, environment strip with Pass / Fail,
  description (sanitised HTML), attachments, related open issues, details panel, breadcrumb Issues ▸ Client ▸ Project ▸
  Sprint ▸ Key.
- Shared `app-issue-timeline` + `app-comment-composer` (in /dev/ui), `TimelineApi`; `IssuesApi` workflow methods,
  list filters with repeated params, related issues. Global `.qa-rich` styles for sanitised rich text.
- API: `IssueDto.deployments`; failed retest note written once. ADR-025.
- Tests: 10 Angular tests (filters URL round trip and API query, action labels, environment strip, mention query,
  mention highlighting, relative time, detail page actions = allowedActions + one-click pick, developer banner during
  Testing, Pass / Fail replacing Verify / Reopen); workflow integration test checks `deployments`.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 211/211 · `ng build` clean · `ng test` 99/99 · `ng lint` clean ·
in Chrome on a separate test project (UI Check / UICHK, sprint "Sprint 1 - UI check" completed for this): developer
`ui.developer` picked UICHK-1, marked it fixed on UAT 1 (1.8.0), commented with an @mention picked from the
suggestions; tester `ui.tester` failed the retest with a pasted screenshot (Reopened 1×), the developer fixed again
(1.8.1, via the API), the tester passed the retest (Verified) and closed it – every step in the timeline with names;
bulk-assigned UICHK-2 to the developer from the list.

**Deviations from plan:** list is mat-table without a column chooser (S31 polish); saved views local only (as planned
"local first"); screenshots on reopen go into a comment.

**Carry-over / known gaps:**
- Local dev DB has test data: accounts `ui.tester` and `ui.developer` (deactivate when not needed), client "UI Check
  Client" / project UICHK with UICHK-1 (closed) and UICHK-2 (assigned), PGERP-3 from S11. Akash's "Sprint 1 – UAT" on
  PGERP was not changed.
- Real-time timeline updates (SignalR) – S18; notifications – S18/S19.
- Malformed (non-UTF-8) JSON → 500 still open (hit again while seeding test data with an en dash from the shell).
- Chrome automation: screenshots still time out right after in-page navigation (page responsive) – as in S07.

**Libraries added (licence):** none.

**Next session:** S16 – My work, retest queue & deploy notice API.

---

## S16 – My work, retest queue & deploy notice API   (28-Oct-2026)

**Done:**
- Application `Features/WorkQueues`: `MyWorkHandler` (five tabs with counts), `RetestQueueHandler` (grouped by
  environment, `canRetest` from IssueWorkflow), deploy notices (candidates, recent list, create – all or nothing).
- `WorkflowSupport.Deployed` / `Fixed` extracted from Mark fixed and reused by deploy notices.
- Domain: `IssueWorkflow.FromStatuses(action)`.
- API `WorkQueuesEndpoints`: `GET /api/me/work`, `GET /api/retest-queue`, `GET /api/deploy-notices/candidates`,
  `GET /api/deploy-notices`, `POST /api/deploy-notices`. ADR-026.
- Tests: 4 integration tests (tabs only after testing completed + need info answered / reopened / overdue / tester has
  nothing to pick; queue groups and order, canRetest per role, pass clears, mine filter; deploy notice on LIVE for a
  Fixed and a Verified issue with one timeline entry each and queue entries; all-or-nothing rejection with reasons,
  same-environment pending, tester, validation) and 1 domain test.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 216/216.

**Deviations from plan:** deploy notice only for Fixed/Verified issues (In Progress issues are marked fixed one by one
with their own note); the tester notification per notice is stored as the DeployNotices row and sent from S18.

**Carry-over / known gaps:** notifications (S18); invalid enum value in a JSON body still answers 500 (same as the
malformed-JSON gap from S08 – hit again by a test typo).

**Libraries added (licence):** none.

**Next session:** S17 – My work, retest queue & deploy notice UI.

---

## S17 – My work, retest queue & deploy notice UI   (28-Oct-2026)

**Done:**
- `features/work`: `MyWorkPage`, `RetestQueuePage`, `DeployNoticePage`, shared `WorkItemRow`; `shared/data/work.api.ts`
  (`WorkApi`, ageing helpers). Routes replace the S17 placeholders; new nav item "Deploy notice" (Developer, Admin).
  ADR-027.
- Tests: 6 Angular tests (ageing buckets, tab from URL + overdue, candidate grouping/blocking, My work tab counts +
  Pick, retest queue Open link + Pass request, empty queue with mine filter).

**Verified:** `ng build` clean · `ng test` 105/105 · `ng lint` clean · `dotnet test` 216/216 (S16). **Milestone M2** in
Chrome on the UI Check project (UICHK, "Sprint 2 - UI check"): tester logged 4 issues and completed testing (API);
developer `ui.developer` picked UICHK-3 from My work › Available; fixes for UICHK-3/4/5 on UAT 1 (API); tester
`ui.tester` passed UICHK-3 and failed UICHK-4 ("what still fails") in the retest queue – reopened back to the
developer; developer sent a deploy notice for UICHK-3 (Verified → Fixed) and UICHK-5 on LIVE – the tester's queue
shows UAT 1: UICHK-5 and LIVE: UICHK-3, UICHK-5, the timeline "deployed the fix on LIVE (version 2.1.0)".
Fixed while checking: due date wrapping, a stray scrollbar on the tab strip, "Saving…" on Pass while the Fail dialog
was open, the "LIVE · LIVE" environment label.

**Deviations from plan:** none in scope. Marking fixed still happens on the issue detail page (S15).

**Carry-over / known gaps:**
- Local dev DB: test accounts `ui.tester` / `ui.developer` got a new local password this session (deactivate when not
  needed); UICHK has a LIVE environment, sprint "Sprint 2 - UI check" and UICHK-3…6.
- Deploy-notice notification to the tester and live queue updates – S18.
- Invalid enum value in a JSON body → 500 (S08 gap) still open.

**Libraries added (licence):** none.

**Next session:** S18 – SignalR & in-app notifications.

---

## S18 – SignalR & in-app notifications   (29-Oct-2026)

**Done:**
- Application `Features/Notifications`: `ChangeFeed` (notification rules of plan 8.3 + realtime events from every
  save), list / unread count / mark read handlers; `IRealtimePublisher` + event records.
- Infrastructure `ChangeFeedInterceptor` (save + transaction interceptor, publishes after commit), `NoRealtimePublisher`.
- API: `TrackerHub` at `/hubs/tracker` (user + issue groups, token from the query string on /hubs),
  `SignalRRealtimePublisher`, `NotificationsEndpoints` (`GET /api/notifications`, `/unread-count`, `POST /read`).
  ADR-028.
- Angular: `RealtimeService` + `reloadOnIssueChanges`, `NotificationStore`, `app-notification-bell` in the top bar,
  `app-notification-item` (also on /dev/ui), `/notifications` page; live updates on issue list, issue detail, timeline,
  My work, retest queue, deploy notice; `/hubs` proxied with WebSockets; `relativeTime` moved to the date pipe module.
- Tests: 5 integration tests (who gets what and only once – testing completed, need info, mention, fixed, reopened;
  deploy notice one per batch; history paging / read / unread / other users' ids; hub pushes comment, notification
  with stored id, issue change and read-elsewhere over a real SignalR client; anonymous hub rejected) and 5 Angular
  tests (link + day grouping, store live arrival + optimistic read, bell badge/panel/open, page paging + live + mirrored
  read state).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 84 + 88 + 49 · `ng build` · `ng test` 110/110 · `ng lint`.
In Chrome as `ui.developer` while `ui.tester` commented through the API: the badge went up live, the panel showed the
mention and comment (read/unread, type icons), opening the mention marked it read and opened UICHK-4, a further
comment appeared in that timeline without a reload and raised the badge; Notifications page grouped Today / Yesterday;
dark theme checked. Fixed while checking: badge covering the bell, list alignment.

**Deviations from plan:** notifications are written by one interceptor instead of "publish from handlers" (same
effect, nothing to forget in new handlers). CriticalUnassigned / Overdue need a scheduler (S19 / S28).

**Carry-over / known gaps:** email (S19); Kanban live board (S20). The local test accounts `ui.tester` /
`ui.developer` got a new local password again (deactivate when not needed). Invalid enum in JSON → 500 still open.

**Libraries added (licence):** `@microsoft/signalr` 10.0.11 (MIT, npm); `Microsoft.AspNetCore.SignalR.Client`
10.0.14 (MIT, integration tests only). The SignalR server is part of ASP.NET Core.

**Next session:** S19 – Email notifications.

---

## S19 – Email notifications   (29-Oct-2026)

**Done:**
- Script `005_EmailDelivery.sql` (Notifications.EmailedAt, EmailOutbox.NextAttemptAt, JobRuns, digest index);
  re-scaffolded (JobRun entity).
- Application: `EmailOptions`, `NotificationChannels` (instant vs digest per type), `NotificationMailer` (queues
  instant mails in the ChangeFeed save, respects preferences), `DailyDigestHandler`, `EmailTemplates` (notification,
  digest, test), preferences handlers, `Features/EmailAdmin` (status, test mail, run digest, retry).
- Infrastructure: `SmtpEmailSender`, `EmailOutboxProcessor` (back-off, max attempts), `EmailOutboxWorker`,
  `DailyDigestWorker` (18:00 IST, catch-up after restart).
- API: `GET/PUT /api/notifications/preferences`, `/api/admin/email` (+ `/test`, `/digest`, `/outbox/{id}/retry`,
  admin only). ADR-029; ADR-019 marked decided for the S19 part.
- Angular: `/settings/notifications` (switch per type, grouped "Mailed right away" / "In the daily summary"), admin
  `/admin/email` (status line, test mail with the SMTP answer, settings in use, outbox counts, not delivered + Try
  again, latest mails, daily summary + "Send the summary now"); nav item "E-mail" (Admin); links from the user menu,
  bell panel and Notifications page. Modern thin scrollbars app-wide (Akash's request during the session).
- Tests: 5 integration tests (instant mails with the notification + preferences; digest of comments and overdue,
  once; digest slot; outbox send / back-off / admin retry; admin status without password, SMTP failure answer,
  validation, 403) and 2 Angular tests (settings page, admin page). Test DB builder now runs a script's GO batches on
  one connection (a transaction may span them, as with sqlcmd).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 226/226 (89 integration + 88 domain + 49 application) ·
`ng build` · `ng test` 112/112 · `ng lint`. With smtp4dev (localhost:2525): on API start the daily summary caught up
the missed slot and arrived ("2 new comments"); a mention arrived at once; after switching mention mails off the next
mention was not mailed; the admin test mail was accepted in 12 ms and shown on the E-mail screen; outbox counts and
digest run displayed. Scrollbars checked on the sidebar and page edge.

**Deviations from plan:** no Hangfire (hosted services + JobRuns, see ADR-029). In-app Overdue / CriticalUnassigned
notifications still need the SLA job (S28); the digest already lists overdue issues.

**Carry-over / known gaps:**
- Local dev DB: new test admin `ui.admin` (same local password as `ui.tester` / `ui.developer`; deactivate when not
  needed); digest JobRuns row and a few outbox mails from the check.
- SMTP settings editable in the app – S28 (today appsettings + user secrets).
- Invalid enum in JSON → 500 still open.

**Libraries added (licence):** `Rnwood.Smtp4dev` 3.15.0 (BSD-3-Clause) as a local dotnet tool for development only
(dotnet-tools.json); no runtime libraries (SMTP via System.Net.Mail).

**Next session:** S20 – Kanban board.

---

## S20 – Kanban board   (29-Oct-2026)

**Done:**
- Application `Features/Issues/Board.cs` (`BoardHandler`, columns per status with counts, cards with `moves` from
  IssueWorkflow); API `GET /api/board`. ADR-030.
- Angular `/board`: filter bar in the URL (client, project, sprint, assignee incl. me/unassigned, severity, search,
  Show Closed and Rejected), lanes with status rail and counts, cards (severity edge, key, title, module › screen,
  priority, due date red when overdue, reopen count, assignee avatar or dashed "unassigned"), drag and drop with
  reachable-column highlight, refusal message, direct actions / confirm for Close / `ActionDialog` for inputs, card
  menu with the same moves, pending card state, live reload, "N more in the issue list" link, "Open as list".
  `DIRECT_ACTIONS` exported from the issue page; `IssueRef` for workflow calls.
- Tests: 2 integration tests (columns + moves per role and sprint state, filters; 400/404) and 6 Angular tests
  (URL filters, moves and refusal texts, pending move, page columns + menu move, refused drop).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 228/228 · `ng build` · `ng test` 118/118 · `ng lint`.
In Chrome as `ui.developer` on UICHK: dropping a Reopened card on New was refused ("UICHK-4 can't go from Reopened to
New. It can go to In Progress."); dropping it on In Progress started it and a second tab moved it live; dropping on
Need Info opened "Ask for information" and Cancel put the card back. No horizontal page scroll (board scrolls inside).
Dark theme not re-checked in the browser (tab group closed during the check) – uses tokens only.

**Deviations from plan:** none. Closed/Rejected columns hidden by default (toggle).

**Carry-over / known gaps:** dark-mode look of the board to eyeball in S22's browser pass. Local test accounts
`ui.*` got a new local password again (deactivate when not needed). Invalid enum in JSON → 500 still open (S30).

**Libraries added (licence):** none (`@angular/cdk/drag-drop` is part of @angular/cdk, already used).

**Next session:** S21 – Analytics API.

---

## S21 – Analytics API   (29-Oct-2026)

**Done:**
- Domain: `WorkingCalendar.WorkingHoursBetween / WorkingDaysBetween / IstDate`.
- Application `Features/Analytics`: shared filter + range + status groups (`AnalyticsCommon`), `OverviewHandler`
  (9.1), `SprintDashboardHandler` (9.2 incl. burn-down and sprint comparison), `DevelopersHandler` (9.3 incl. My
  stats), `QualityHandler` (9.4), `ClientsHandler` (9.5 incl. health rule). API `AnalyticsEndpoints`. ADR-031.
- Tests: 4 domain tests (working hours between, Sundays/holidays, inverse of AddWorkingHours, IST date) and 4
  integration tests (a played sprint – closed, reopened, rejected, untouched – checked on all five dashboards; overdue
  turns the client amber; filter validation and 404; health and ageing rules; 5 000 issues < 800 ms per endpoint).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 236/236. Timings on 5 000 issues (local SQL Express): overview
≈ 250 ms, sprint ≈ 180 ms, developers / quality / clients 30–60 ms.

**Deviations from plan:** the date range applies to time-based widgets only (ADR-031). Workload counts Reopened under
Assigned.

**Carry-over / known gaps:** none for the API; the UI comes in S22–S24. Invalid enum in JSON → 500 still open (S30).

**Libraries added (licence):** none.

**Next session:** S22 – Chart components & Overview dashboard.

---

## S22 – Chart components & Overview dashboard   (29-Oct-2026)

**Done:**
- `shared/charts`: lazy ECharts setup (`provideCharts`), `ChartTheme` (tokens, repaint on theme change), option
  builders, `app-chart` frame (states, table view, click), wrappers bar / stacked bar / donut / line / heatmap /
  treemap / gauge, `app-kpi-card`, `app-dashboard-filters` (URL-synced scope + period, `IstDateAdapter`).
- `shared/data/analytics.api.ts` (overview types).
- Overview dashboard (`features/dashboards/overview-page`) as Home and Dashboards › Overview with click-through to
  the filtered issue list; chart kit added to /dev/ui. ADR-032.
- Fix: dates print "Sep" (not "Sept") everywhere (`formatIst`).
- Tests: 11 chart-kit tests (bar/stack/donut/line/heatmap options, compact numbers, URL filters, IST periods, date
  adapter, trend text) and 3 overview tests (items, click-through params, page loads filters + KPIs + activity);
  shell tests updated (Home heading is "Overview").

**Verified:** `ng build` (initial 720 kB; ECharts lazy chunk 667 kB raw / 191 kB transfer) · `ng test` 131/131 ·
`ng lint` · `dotnet test` unchanged (236). In Chrome as `ui.developer`: Home shows all widgets with live numbers; the
High bar opened `/issues` with open statuses + High and the list had the same 4 issues; dark theme repaints the
charts; no horizontal page scroll after confining tooltips; /dev/ui shows every chart and state. Board checked in dark
theme (S20 carry-over).

**Deviations from plan:** the created-vs-closed line has no click-through (no date filter in the issue list).

**Carry-over / known gaps:** S23 sprint & developer dashboards, S24 quality & client health (API ready, kit ready).
Severity colours High/Medium are close for colour-blind readers – always keep labels/legends (ADR-032). Local `ui.*`
test accounts got a new local password (deactivate when not needed). Invalid enum in JSON → 500 still open (S30).

**Libraries added (licence):** `echarts` 6.1.0 (Apache-2.0), `ngx-echarts` 22.0.0 (MIT).

**Next session:** S23 – Sprint & Developer dashboards.

---

## S23 – Sprint & Developer dashboards   (29-Oct-2026)

**Done:**
- Sprint dashboard (`features/dashboards/sprint-dashboard-page`) and Developer dashboard (`developers-page`) on
  `/dashboards/sprint` and `/dashboards/developers` (routes share `chartRoute`); "Sprint dashboard" link on the Testing
  page. ADR-033.
- Analytics client types for both endpoints; categorical chart tokens `--qa-chart-1…6`; chart-kit additions (bar
  `format` / `max` / `zeroIsEmpty`, selective labels, KPI string values, filter bar without Period).
- Tests: 9 dashboard tests (newest sprint, progress / burn-down / heat map / comparison data, click-through params,
  workload + Others folding + metric labels, sprint page header, My stats for developers, hidden for testers) and 1
  chart-kit test (0 as a metric value vs empty counts).

**Verified:** `ng build` (initial 721 kB) · `ng test` 141/141 · `ng lint` · no backend change (`dotnet test` 236 from
S21). In Chrome as `ui.developer` on UICHK: the sprint dashboard opened the newest sprint; heat-map cell Billing ×
Critical (1) → issue list with 1 row; workload Assigned part (1) → issue list with 1 row; dark theme checked. Fixed while
checking: a column label clipped at the top; "Average fix time" showed "Nothing was fixed" for a real 0 h average.

**Deviations from plan:** type chart is a bar and sprint comparison stacked (ADR-033); no click-through for type and
found-per-day (no such issue-list filters). The Sprints page link waits for S29 (linked from Testing instead).

**Carry-over / known gaps:** an issue-list filter for type and created date would allow the missing click-throughs
(consider in S31). Local `ui.*` test accounts still have the local password from S20 (deactivate when not needed).
Invalid enum in JSON → 500 still open (S30).

**Libraries added (licence):** none.

**Next session:** S24 – Quality & Client health dashboards.

---

## S24 – Quality & Client health dashboards   (29-Oct-2026)

**Done:**
- Quality dashboard (`features/dashboards/quality-page`) and Client health (`clients-page`) on `/dashboards/quality`
  and `/dashboards/clients` (placeholders replaced). ADR-034.
- Analytics client types and calls for both endpoints; `app-health-badge` (shared/ui, added to /dev/ui); chart kit
  `app-chart-line [stacked]` for the severity trend.
- Tests: 8 helper tests (leakage by phase, severity trend, item labels, quality click-through, red-first client order,
  client × severity + click-through, UAT vs LIVE parts, closure items), 2 page tests (quality KPIs incl. alert and
  ratio; client cards with health words, last sprint and dd/MMM/yyyy) and 1 chart-kit test (stacked areas).

**Verified:** `ng build` (initial 721 kB) · `ng test` 151/151 (3 of 4 runs; one run under load had 3 failures that did not repeat) · `ng lint` · no backend change (`dotnet test` 236 from
S21). In Chrome as `ui.developer`: both dashboards load with live numbers; the Billing hotspot (6) opened the issue list
with 6 issues; client cards (3 open each) match the open-by-severity bars and the issue list; dark theme checked; no
horizontal page scroll. **Milestone M4 – all five dashboards.**

**Deviations from plan:** UAT vs LIVE stacked, not grouped; no click-through for leakage, UAT vs LIVE, turnaround and
the trend (no matching issue-list filters) – ADR-034.

**Carry-over / known gaps:** issue-list filters for type, phase, environment and created date would allow the missing
click-throughs (consider in S31). Local `ui.*` test accounts got a new local password again (deactivate when not
needed). Invalid enum in JSON → 500 still open (S30).

**Libraries added (licence):** none.

**Next session:** S25 – Sprint report export.

---

## S25 – Sprint report export   (29-Oct-2026)

**Done:**
- API: `GET /api/sprints/{id}/report?format=xlsx|pdf`, `GET /api/issues/export` (list filters + sort). ADR-035.
- Application `Features/Reports`: `SprintReportHandler` (report model, summary, thumbnails, closed-sprint snapshot),
  `ExportIssuesHandler` (reuses the list filter/sort, 10 000-row cap, filters described in words), `ReportText`
  (IST dates, HTML → lines), `ReportNames`.
- Infrastructure `Reports`: `ExcelReportWriter` (summary, a sheet per module like the legacy workbook with
  screenshots, all issues; issue-list export), `PdfReportWriter` (SkiaSharp PDF: cover, summary bars and table, issues
  with thumbnails), `ReportImages` (WebP thumbnails → PNG), `ReportRenderer`. CORS exposes `Content-Disposition`.
- Web: `app-sprint-report-button` on the sprint dashboard (and /dev/ui), "Export" on the issue list,
  `shared/data/download.ts` (blob GET, file name from Content-Disposition, save).
- Tests: 4 integration (Excel sheets / module layout / status columns / picture / real dates; PDF valid, ≥ 3 pages,
  image embedded; 400 / 404; export filters, sort, no paging, description), 4 Application (HTML → lines, IST dates,
  file names, summary counts), 6 web (file name parsing, save link, export params, fallback name, button progress
  and error toast).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 244/244 · `ng build` · `ng test` 157/157 · `ng lint`. Real
reports for UICHK Sprint 2: the PDF (1.1 MB) rendered cleanly in a PDF viewer – cover, summary, 4 issues with severity
chips; the workbook was opened and checked with ClosedXML in the tests (not opened in desktop Excel in this session).
In Chrome as `ui.developer`: "Download report › Excel" delivered "UICHK Sprint 2 - UI check report 29-Oct-2026.xlsx";
"Export" on the issue list (Billing, High: 3 of 3) delivered a workbook with the same 3 issues (link click stubbed,
nothing saved to disk).

**Deviations from plan:** PDF with SkiaSharp (MIT) instead of QuestPDF – the Community licence eligibility is not
confirmed (ADR-035). Sprint report button on the sprint dashboard; the Sprints page is S29.

**Carry-over / known gaps:** open both files once in desktop Excel / Acrobat before sending to a client. PDF size is
mostly embedded fonts (~1 MB). Linux hosting needs a UI font for the PDF. Closed-sprint numbers come from
SprintSnapshots, which S29 must write on close. Local `ui.*` accounts have a new local password (deactivate when not
needed). Invalid enum in JSON → 500 still open (S30).

**Libraries added (licence):** `ClosedXML` 0.105.0 (MIT) with DocumentFormat.OpenXml 3.1.1 (MIT), ClosedXML.Parser
2.0.0 (MIT), ExcelNumberFormat 1.1.0 (MIT), RBush.Signed 4.0.0 (MIT), SixLabors.Fonts 1.0.0 (Apache-2.0).

**Next session:** S26 – Excel import API.

---

## S26 – Excel import API   (29-Oct-2026)

**Done:**
- API `POST /api/import/excel/preview` and `/commit` (ImportEndpoints). ADR-036.
- Application `Features/Import`: raw workbook model + `ILegacyWorkbookParser`, `LegacyImportMapper` (Appendix C rules,
  corrections), `LegacyImportReader`, `ImportPreviewHandler`, `ImportCommitHandler` (modules, numbering, statuses,
  activities, comments, screenshots).
- Infrastructure `Import/ClosedXmlLegacyWorkbookParser` (header detection, issue blocks, labels, severity | priority,
  status columns, other texts, anchored pictures).
- Tests: 22 mapper unit tests (severity / priority words, first-name matching incl. ambiguous / several / unknown,
  status precedence, missing actual result, long titles, module matching, corrections, HTML escaping) and 3 integration
  tests on a workbook built like the legacy layout (preview of 2 sheets / 5 blocks incl. multi-row block, `<br>`,
  "Not yet assessed", Invalid, Pending Reason, picture, "For your refer." comment; commit with module rename, skip and
  corrections → issues, statuses, VerifiedAt, attachment, activities; refusals: developer 403, not a workbook / wrong
  extension / bad JSON / unknown row 400, unknown sprint 404, sprint no longer in Testing 422).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 269/269. No frontend change (Import UI is S27).

**Deviations from plan:** the card's done-when ("real workbook parses with ≥ 95 % rows needing no manual
correction") **could not be checked** – the anonymised copy of the real "Pardi Gears Testing Sheet" is not in the
repository. The parser follows Appendix C and is tested on a synthetic copy of that layout. Note: by Appendix C a
"Not yet assessed" / "Pending…" severity or priority is itself a warning, so such rows count as needing correction.

**Carry-over / known gaps:** run the preview on the anonymised real workbook (S27 or before) and tune the parser if
needed; screenshots written before a failed commit could stay on disk (transaction rolls back the rows only).
Local `ui.*` accounts have a new local password (deactivate when not needed). Invalid enum in JSON → 500 still open
(S30).

**Libraries added (licence):** none (ClosedXML from S25).

**Next session:** S27 – Import UI.

---

## S27 – Import UI   (29-Oct-2026) – partly done, carry-over S27b

**Done:**
- Import wizard `features/import` (`import-page`, `import-edits`) on `/import` with a sidebar item for testers and
  admins; `shared/data/import.api.ts`. ADR-037.
- Tests: 4 helper tests (corrections only for changes, unassign, warnings settled by edits, file checks) and 2 page
  tests (drop → remembered project's Testing sprint → preview request → review counts and "To review" filter → skip a
  row → commit body → summary text and issue-list link; a rejected file and a 400 from the preview shown inline).
- Sample workbook generator in the legacy layout: `tests/fixtures/legacy-workbook/make-sample-workbook.cs`.
- Fixed while testing: a missing space in "Into Sprint 1 – UAT (in Testing)" (Angular drops whitespace-only nodes).

**Verified:** `ng build` (initial 723 kB) · `ng test` 163/163 · `ng lint`. No backend change (`dotnet test` 269 from
S26).

**Not done (stopped on request – moved to S27b):** the card's done-when, an end-to-end import in the browser, has not
been run yet; no Chrome check of the wizard (light/dark, phone width). Milestone M5 waits for S27b.

**Carry-over / known gaps:** S27b (plan 16.3). The anonymised real workbook is still needed for the S26 ≥ 95 %
target. Local `ui.*` accounts have a new local password (deactivate when not needed). Invalid enum in JSON → 500 still
open (S30).

**Libraries added (licence):** none.

**Next session:** S27b – Import end-to-end check, then S28.

---

## S27b – Import end-to-end check   (30-Oct-2026)

**Done:**
- End-to-end import in Chrome as `ui.tester`: sample workbook (generator in `tests/fixtures/legacy-workbook`) →
  UI Check Client → quick-added project "Import Check" (UCCIC) → started "Sprint 1 – Early Testing" from the wizard →
  review: renamed module "Item Mast, BOM, Tax Str.Mast" → "Item Masters", set the not-assessed severity to High
  (warning settled), skipped one row → "Import 4 issues" → UCCIC-1…4, 2 screenshots, 2 new modules, 1 row skipped.
  Issue list (4 issues, statuses Assigned / Verified / On Hold / New, renamed module), UCCIC-1 screenshot and
  "assigned to" activity, UCCIC-2 "verified the fix" activity and the imported "For your refer." comment all checked.
  Dark theme checked.
- Fixed from the check: counts in the wizard used fixed plurals ("1 rows skipped", "Import 1 issues") – now the
  `plural` pipe throughout; review-grid selects were too narrow for "Not assessed" and full developer names (text ran
  into the arrow) – 9.5 rem, assignee 12 rem; the entity picker re-opened its list after a quick-add (the dialog
  returns focus to the input, which opens the autocomplete) – the panel is now closed after the choice.
- Sample workbook generator ran into Central Package Management (NU1008) – `#:package` lines without versions now
  take the versions from Directory.Packages.props.
- Flaky web tests (random "Test timed out in 5000ms", first test of a file paying for the jsdom set-up): `ng test`
  now loads `vitest-base.config.mts` (angular.json `runnerConfig`) with a 15 s test timeout.

**Verified:** `ng build` (initial 723 kB) · `ng test` 163/163 (twice in a row) · `ng lint`. No backend change (`dotnet test` 269 from
S26). Preview timing: 2.2 s cold after an API restart, 0.03 s warm (the first run in the session took 37 s while the
API and the Angular dev server were still building – machine load, not reproducible). Wizard at 1024 px: the grid
scrolls inside its card (1 419 px content in a 716 px card), no horizontal page scroll. **Milestone M5 – import.**

**Deviations from plan:** "phone width" in the card was checked at 1024 px instead – plan 10.x sets responsive down to
tablet (1024 px); the shell has no phone layout (sidebar stays open). The anonymised real "Pardi Gears Testing Sheet"
is still not available, so the S26 ≥ 95 % target remains unchecked. The picker fix has no unit test – jsdom does not
reproduce the dialog's focus return (checked in the browser).

**Carry-over / known gaps:** run the preview on the real anonymised workbook when it is provided. A phone layout for
the shell (drawer sidebar) would be new scope – S31 at the earliest. Test data left in the local DB: project UCCIC with
4 issues. Local `ui.tester` / `ui.admin` have a new local password (deactivate when not needed). Invalid enum in
JSON → 500 still open (S30).

**Libraries added (licence):** none.

**Next session:** S28 – SLA, overdue & settings.

---

## S28 – SLA, overdue & settings   (30-Oct-2026)

**Done:**
- DB script `006_SettingsAndSmtp.sql` (RowVersion on SlaPolicies / SprintPhases, table EmailSettings); re-scaffolded.
- Application `Features/Settings` (SLA, holidays, phases incl. reorder), `EmailAdmin/MailServerSettings`
  (`EmailSettingsSource`, get / save / reset), `Notifications/SlaAlerts` (overdue + critical unassigned).
  Infrastructure: `SlaAlertsWorker` (hourly), SMTP sender uses the effective settings. API `SettingsEndpoints`,
  `/api/admin/email/settings` (GET/PUT/DELETE). ADR-038.
- Web: `/admin/settings` (SLA with bars on one scale, Holidays, Sprint phases), sidebar "Settings", "Edit mail
  server" dialog and source line on Admin › E-mail, `app-overdue-badge` in list / board / detail and /dev/ui.
- Tests: integration – SLA change gives a new due date within 1 working hour (and 400 / 403 / 409), holidays
  (201 / 409 / 204 / 404), phases (add, duplicate, reorder, rename + switch off hides from the lookup, 409), SLA
  alerts fire once (overdue for the assignee, critical unassigned for both members, not for an issue unassigned 30 min
  ago; critical mailed instantly, overdue not) and overdue fires again for a new due date; SMTP settings saved in the
  app are used by the next test mail (fake SMTP server checks the envelope), password never returned, 409 on a second
  first save, reset. Web – working-day text, overdue label, SLA bars and payload, holidays by year, phase reorder
  payload, mail-server dialog password keep / remove.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 274/274 · `ng build` (initial 724 kB) · `ng test` 169/169 ·
`ng lint`. In Chrome as `ui.admin`: Settings tabs (SLA bars 7 / 14 / 21 / 28 / 35 working days, phases with sprint
counts, holidays empty state), mail-server dialog (fixed: the sign-in hint overlapped the sender field), overdue
badges in /dev/ui. The local SMTP settings were not changed (dialog cancelled).

**Deviations from plan:** none in scope. SLA / holiday changes are not applied to existing due dates (stated in the
UI, ADR-038). The browser check did not save settings or wait for a live hourly alert – those paths are covered by
the integration tests.

**Carry-over / known gaps:** "Due date editable by tester" (plan 8.4) is still not built. The IST date adapter lives
in `shared/charts/dashboard-filters.ts` (now also used by Holidays – could move to shared/dates in S31). Local
`ui.tester` / `ui.admin` have a new local password (deactivate when not needed). Invalid enum in JSON → 500 still open
(S30).

**Libraries added (licence):** none.

**Next session:** S29 – Sprint close & carry forward.

---

## S29 – Sprint close & carry forward   (31-Oct-2026)

**Done:**
- DB script `007_SprintCloseAndCarryForward.sql` (SprintEvents kinds IssuesCarriedForward / IssuesCarriedIn,
  `SprintSnapshots.CarriedToSprintId`); re-scaffolded. ADR-039.
- Domain `SprintRules.CanCarryForward`. Application `Sprints/CloseSprint.cs` (`CarryForwardStep`, carry-forward and
  close handlers – close can carry the open issues in the same transaction), `OpenIssuesHandler`,
  `SprintDto.CarriedForwardCount`; snapshot rows frozen when an issue leaves and at closing; timeline line for
  CarriedForward; carried issues cannot be deleted. API `POST /api/sprints/{id}/carry-forward`, `POST …/close`,
  `GET …/open-issues`.
- Sprint report: a closed sprint is built from its snapshot only; carried-out issues stay in the report as they were
  when they left ("Carried forward to" in Excel status cell and "All issues", PDF meta line, summary count).
- Web: `/sprints` page (was a placeholder) with the lifecycle track, progress, next-step actions, report, history;
  `CloseSprintDialog` (close / carry mode, "Start new sprint" when no target), `ReopenTestingDialog` (admin);
  shared `app-sprint-status` (in /dev/ui, now also on the sprint dashboard); timeline shows the carry note.
- Tests: domain (carry-forward role/state/target rules); integration – close blocked while issues are open, 403 for
  developers, finished / same-sprint refused, open-issues list, close with carry-forward (keys kept, due date cleared
  into a Testing sprint, snapshot with target, both histories, timeline line), **report after the issues moved on
  still shows status / assignee / severity as of closing** (done-when), second close 422, delete of a carried issue
  422; carry into a completed sprint keeps the due date, stale version 409, unknown target 404, empty list 400.
  Web – lifecycle / progress / target helpers, dialog payloads (close + carry, plain close, carry chosen only,
  start-new-sprint), page actions by role, history.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 290/290 · `ng build` (initial 726 kB) · `ng test` 179/179 ·
`ng lint`. In Chrome as `ui.tester` on project UCCIC: Mark testing completed → Close sprint → "no open sprint" →
Start new sprint ("Sprint 1 – Early Testing Round 2") → back in the dialog with it as target → "Carry forward 4 and
close" with a note → closed card "All 4 issues carried forward", history (started, completed, carried with the keys
and note, closed), dark theme (fixed: the Closed chip was 3.7:1 on the dark surface).

**Deviations from plan:** the carry-forward body sends `{id, rowVersion}` per issue instead of bare `issueIds`
(ADR-039). Reopen testing UI is in the ⋮ menu of a Testing-Completed sprint (admin only).

**Carry-over / known gaps:** flaky under load – `AnalyticsTests.Endpoints_AnswerWithin800ms_On5000Issues` failed in
2 of 4 full runs (a 31 s DB wait; passes alone and in the other runs) and one web test timed out once ("admin opens
users page", dev servers running) – S30/S31 look at both. Test data in the local DB: UCCIC sprint 1 closed, 4 issues
carried to "Sprint 1 – Early Testing Round 2". Local `ui.*` accounts got a new local password (deactivate when not
needed). Invalid enum in JSON → 500 still open (S30).

**Libraries added (licence):** none.

**Next session:** S30 – Security & integration test pass.

---

## S30 – Security & integration test pass   (01-Nov-2026)

**Done:**
- Malformed bodies answer 400 ProblemDetails (`malformed_request`) instead of 500 – invalid enum value, non-UTF-8
  bytes, truncated / non-JSON body, wrong types, unknown charset, unparsable query values (**open since S08 – closed**).
  `BadRequestExceptionHandler`, `RequestCharsetGuard`, `ThrowOnBadRequest` in every environment. ADR-040.
- Security headers on every response (`SecurityHeaders`), no Server banner; global per-user rate limit (600/min) and
  the sign-in limit now also covers password change; 429 as ProblemDetails with Retry-After.
- `SecurityTests` (15): anonymous sweep over every endpoint (401 except the reviewed public ones, hub negotiate too),
  reviewed policy list for all 102 endpoints (`Security/endpoint-policies.txt`), developer / tester refused on 18
  role-restricted calls across masters, sprints, issues, attachments, import, users, settings and mail, comment edit
  by others (developer and admin → 403 `not_author`), six malformed-body cases + query values, headers, rate limits
  (login, password change, API), upload names (path traversal stays a display name, random storage path, script
  disguised as PNG refused). Existing ownership tests (notifications, signed attachment URLs) reviewed – kept.
- Playwright E2E (`npm run e2e`): fresh `QaTracker_E2E` DB, API :5129, ng serve :4300, installed Chrome. Flows:
  tester logs an issue with a pasted screenshot and marks testing completed → developer picks and marks it fixed on
  UAT → tester passes it from the retest queue (Verified).

**Verified:** `dotnet build` 0 warnings · `dotnet test` 305/305 · `ng build` (initial 725 kB) · `ng test` 179/179 ·
`ng lint` · `npm run e2e` 4/4 (setup + 3 flows, 1.4 min).

**Deviations from plan:** 15.1 says Testcontainers – the suite keeps the `TestDatabase` fixture on .\SQLEXPRESS (as
since S03). E2E is not part of `npm test` (needs SQL Express). Pick assigns and starts work in one step, so the
developer flow has no separate "Start work".

**Carry-over / known gaps:** `ng lint` does not cover `e2e/` (Prettier does). The flaky
`AnalyticsTests.Endpoints_AnswerWithin800ms_On5000Issues` was not hit in this session's full run – S31 (performance).
Git Bash was quarantined once by the K7 antivirus during the session (restored); later commands ran in PowerShell.

**Libraries added (licence):** `@playwright/test` 1.64.1 (Apache-2.0, dev dependency).

**Next session:** S31 – UX polish & performance.
---

## S31 – UX polish & performance   (01-Nov-2026)

**Done:**
- Keyboard shortcuts: `?` help dialog (`app-shortcuts-dialog`, /dev/ui, user menu entry), `/` search, `g` + letter
  navigation by role; the Testing screen's `n` ignores keys the shell already handled. Top-bar search now works
  (key → issue, text → issue list) – it had stayed a disabled placeholder. ADR-041.
- Consistency sweep: every page has loading / empty / error states (dashboards through `app-chart` / `app-kpi-card`);
  unused `placeholder-page` removed; contrast fixed with new text tokens (`text-success/-danger/-warning`), muted text
  and sprint chip colours, error texts `text-danger` in both themes; two ARIA fixes (comment box, user menu).
- Performance: charts `@defer (on viewport)`; script 008 (6 indexes from the missing-index DMVs, one replaced) and
  009 (READ_COMMITTED_SNAPSHOT) – applied locally, re-scaffolded (indexes only); `IssueListPerformanceTests`.
- Found in the browser check: the shell's keydown listener returned `false` for ordinary keys, which Angular turns
  into preventDefault() – no typing anywhere in the shell. Fixed (void wrapper) and the E2E retest flow now types the
  issue key into the search with real keystrokes.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 306/306 · `ng build` (initial 741 kB raw / 178 kB transfer) ·
`ng test` 187/187 · `ng lint` · `npm run e2e` 4/4 (three runs this session, all green) · Lighthouse accessibility
**100** on 14 main pages (tester + developer, light and dark; first run 92–100) · issue list < 500 ms with 1 000 of
3 000 rows matching.

**Deviations from plan:** Lighthouse was run with a scratch script (Lighthouse 12.8.2 from npx, not a project
dependency) against the E2E database, not in CI. Bundle: +16 kB raw / +4.7 kB transfer (search, shortcuts, chunk
re-split) – accepted, budget 1 MB.

**Carry-over / known gaps:** the S29 flaky analytics test did not recur in this session's runs (RCSI should remove
the cause – watch in S32). No phone layout for the shell (from S27b). Admin pages were not in the Lighthouse set (no
admin password kept for the E2E DB). Local DB QaTracker_E2E is left with the last E2E data (recreated every run).

**Libraries added (licence):** none (Lighthouse used via scratch install only).

**Next session:** S32 – Deployment & handover.
---

## S32 – Deployment & handover   (01-Nov-2026)

**Done:**
- The API hosts the Angular build (`WebAppHosting`): one HTTPS site, deep links answer `index.html`, unknown `/api`
  stays 404, hashed bundles cached for a year, page CSP with the hash of Angular's inline script computed at start-up.
  Nothing changes in development / tests (no `wwwroot`). ADR-042.
- Production configuration: `appsettings.Production.json` (seeding + mail on), server secrets and paths in the
  `QATRACKER_SETTINGS` file outside the app folder; IIS `web.config` (in-process, WebSockets, 60 MB request limit, no
  Server / X-Powered-By). `/health` checks the database (503 when it is down).
- `deploy/`: `build-release.ps1` (tests → `ng build` → win-x64 publish → 41 MB zip), `install-server.ps1` (IIS
  features, Hosting Bundle check, app pool AlwaysRunning + preload, HTTPS binding, folders + ACLs, settings file with
  random JWT key and one-time admin password, DB scripts + least-privilege logins, firewall, nightly backup task),
  `deploy.ps1` (pre-deploy backup, maintenance page, DB scripts, `app-previous`, mirror, smoke test, removes the
  temporary admin password), `backup.ps1` (full backup WITH CHECKSUM + VERIFYONLY, attachments tar.gz, 14 days,
  optional off-server copy), `restore.ps1` (live restore or side-by-side drill), `app_offline.htm`.
- `deploy/README.md`: server prerequisites, build, install, deploy, **go-live checklist** (mail, users, masters,
  workbook import, first backup check, Excel retired), rollback, backup / restore, day-to-day.
- `WebAppHostingTests` (9): deep links + CSP hash, bundle caching, `/api` and `/hubs` never answered with the page,
  POST outside the API, API/health unchanged, no fallback without a build.

**Verified:** `dotnet build` 0 warnings · `dotnet test` 315/315 · `ng test` 187/187 · `ng lint` · deploy scripts parse
in Windows PowerShell 5.1 · release zip built · **production trial**: the published release run with
`ASPNETCORE_ENVIRONMENT=Production` + a settings file on a fresh DB (`QaTracker_ProdTrial`, from apply-scripts) –
admin + reference data seeded, `/health` 200, Chrome: sign-in, forced password change, Overview dashboard, lazy
chunks, font, SignalR negotiate, **no CSP violations** · `backup.ps1` against the trial DB (bak + VERIFYONLY + tar.gz)
· `restore.ps1 -TargetDatabase` drill (row counts printed). Trial DBs dropped afterwards.

**Deviations from plan:** IIS chosen, Docker not built (ADR-042). The trial ran on Kestrel (http://localhost), not
IIS – IIS is not installed on the dev machine and the shell was not elevated, so `install-server.ps1`, `deploy.ps1`
and the live branch of `restore.ps1` have only been parse-checked, not executed. `npm run e2e` not run (no
frontend change).

**Carry-over / known gaps (needs the real server – the card's "Done when" is not met yet):**
- Run `install-server.ps1` + `deploy.ps1` on the office server (host name, certificate, SMTP relay to be decided),
  fix whatever the first real run shows, then the go-live checklist: import the current Pardi Gears workbook into
  production, create the real users, retire the Excel sheet. **Milestone M6** after that.
- Pick the off-server backup target (`-BackupCopyTo`) and run the first restore drill on the server.
- Unauthenticated requests to URLs without an endpoint (e.g. a missing `.js` file) answer 401 from the fallback
  policy rather than 404 – harmless, kept.
- From S31: no phone layout for the shell; admin pages not in the Lighthouse set.

**Libraries added (licence):** none.

**Next session:** go-live on the server (carry-over above), then post-launch fixes from the team's feedback.
