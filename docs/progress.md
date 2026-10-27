# QA Tracker – progress log

Plan: 32 sessions (S01–S32), ~2 h each. Milestones: M1 after S11 · M2 after S17 (go live internally) · M3 S20 · M4 S24 · M5 S27 · M6 S32.

| Session | Title | Status |
| --- | --- | --- |
| S01 | Solution & workspace scaffold | ✅ Done (27-Oct-2026) |
| S02 | App shell & shared UI primitives | ✅ Done (27-Oct-2026) |
| S03 | Domain entities & database (database-first) | ✅ Done (27-Oct-2026) |
| S04 | Authentication API (UserName login, plain-text passwords) | ✅ Done (27-Oct-2026) |
| S05 | Authentication UI & users admin | ⏭ Next |
| S06 | Masters API | |
| S07 | Masters UI + entity-picker | |
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
- Due-date rule for severity `NotAssessed` (no SLA row) – decide in S08.
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
