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
| S07 | Masters UI + entity-picker | ⏭ Next |
| S08 | Testing Sprint API | |
| S09 | Issue API – create, edit, list | |
| S10 | Attachments | |
| S11 | Testing workspace UI (M1) | |
| S12 | IssueWorkflow domain | |
| S13 | Workflow API | |
| S14 | Timeline & comments API | |
| S15 | Issue list & detail UI | |
| S16 | My work, retest queue & deploy notice API | |
| S17 | My work, retest queue & deploy notice UI (M2) | |
| S18 | SignalR & in-app notifications | |
| S19 | Email notifications | |
| S20 | Kanban board (M3) | |
| S21 | Analytics API | |
| S22 | Chart components & Overview dashboard | |
| S23 | Sprint & Developer dashboards | |
| S24 | Quality & Client health dashboards (M4) | |
| S25 | Sprint report export | |
| S26 | Excel import API | |
| S27 | Import UI (M5) | |
| S28 | SLA, overdue & settings | |
| S29 | Sprint close & carry forward | |
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
