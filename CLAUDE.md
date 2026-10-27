# QA Tracker – rules for Claude

## Context
- Testing & issue coordination app. Roles: Tester, Developer, Admin.
- Hierarchy: Client > Project (Key) > Module; Environments (UAT/LIVE URLs); Testing Sprints.
- Full spec: docs/plan.md (source: QA_Tracker_Build_Plan.docx). Progress log: docs/progress.md. Decisions: docs/decisions.md.
- **docs/decisions.md overrides docs/plan.md** where they conflict (e.g. database-first instead of code-first).

## Session discipline
- Work ONLY on the session named in the prompt. ~2 hours. Stop when the card is done.
- Start: read this file, docs/progress.md, and the session card in docs/plan.md (section 16.3).
- End: build + tests green, update docs/progress.md (done / deviations / carry-over / libraries / next), commit.
- Git: one branch per session (`s07-masters-ui`), Conventional Commits, squash-merge to main.

## Key business rules
- Sprint states: Testing -> TestingCompleted -> Closed. One Testing sprint per project.
- Only tester/admin add or edit issues, and only while sprint is Testing.
- Developers act on issues only when sprint is TestingCompleted.
- All issue transitions go through Domain/IssueWorkflow. UI uses allowedActions from API.
- Every state/assignment change writes an IssueActivities row. Activities are append-only.
- Pick is atomic. All updates use rowversion (409 on conflict).

## Database (database-first – see docs/decisions.md ADR-001)
- Schema lives in `database/scripts/NNN_Description.sql`, applied in order by `database/apply-scripts.ps1` (tracks `meta.SchemaVersions`). Never edit an applied script – add a new one.
- EF Core model is generated with **Scaffold-DbContext**; the exact command is in `database/scaffold-command.txt`.
  Entities -> `src/QaTracker.Domain/Entities`, DbContext -> `src/QaTracker.Infrastructure/Persistence/QaTrackerDbContext.cs`.
- Never hand-edit scaffolded files; extend via `partial` classes / `OnModelCreatingPartial` in separate files.
- Enum columns: nvarchar + CHECK, mapped to `QaTracker.Domain.Enums` by `src/QaTracker.Infrastructure/CodeTemplates/EFCore/EntityType.t4` (column map) and `QaTrackerDbContext.Conventions.cs` (string conversion). No DEFAULT constraints – code sets every value.
- Table/column names: see ADR-010 (`ProjectEnvironments`, `IssueKey`, `ProjectKey`…).
- Users log in with **UserName** (ADR-011). Passwords are stored **plain text** by product-owner decision (ADR-009): never log or return them; compare in constant time.
- Seeder (`DatabaseSeeder`, idempotent) runs on API start when `Database:SeedOnStartup` is true (Development). Admin password only via user-secrets `Seed:AdminPassword`.
- Connection string: `ConnectionStrings:QaTracker` in `src/QaTracker.Api/appsettings.json` (local `.\SQLEXPRESS`, Windows auth).
- No EF migrations.

## Backend
- .NET 10, Clean layers (Domain, Application, Infrastructure, Api). Feature folders. No MediatR/AutoMapper.
- Async + CancellationToken. Result<T> + ProblemDetails. DTO records. AsNoTracking projections.
- FluentValidation, Serilog, IClock, ICurrentUser. UTC datetimeoffset.
- Central package versions in Directory.Packages.props; warnings are errors.
- Tests: xUnit v3 + Shouldly on Microsoft.Testing.Platform (`dotnet test`). Domain rules fully covered.
  DB tests use the `TestDatabase` fixture (throw-away DB on `.\SQLEXPRESS` built from database/scripts).

## Frontend (qa-tracker-web)
- Angular 22 standalone, zoneless, OnPush, signals, inject(), @if/@for/@defer.
- Angular Material 22 (M3 theme in src/styles/material-theme.scss, ADR-007) + Tailwind 4. Icons: Lucide via @ng-icons (`provideIcons` per component). Tokens in `src/styles/tokens.css`, exposed as Tailwind classes in `src/styles.css`
  (`bg-surface`, `text-muted`, `text-severity-critical`, `rounded-card`…). No hard-coded colours.
- Dark mode = `app-dark` class on <html> (ThemeService); Tailwind `dark:` variant uses the same class.
- Shared UI in src/app/shared/ui (page-header, empty-state, status-pill, severity-badge, priority-icon, user-avatar, skeleton-*, breadcrumb, confirm-dialog); feedback via ToastService / ConfirmService (core/feedback). Living style guide: /dev/ui – add every new shared component there.
- Every page: loading skeleton, empty state, error state. Dates **dd/MMM/yyyy** (27/Oct/2026, `istDate` pipe), IST.
- Type scale is one step above plan 10.2 (body 15 px) and the sidebar is the dark "ink" panel with the app logo (`app-logo`, `public/favicon.svg`) – ADR-014.
- Charts only through shared ECharts wrappers; aggregation server-side.

## Commands
- Backend: `dotnet build` · `dotnet test` · `dotnet run --project src/QaTracker.Api` (http://localhost:5029, Scalar at /scalar)
- Frontend (in qa-tracker-web): `npm start` (http://localhost:4200) · `npm run build` · `npm test -- --watch=false` · `npm run lint` · `npm run format`
- DB: `.\database\apply-scripts.ps1`, then the scaffold command in `database/scaffold-command.txt` (add `--no-build` if code already uses the new columns).

## Never
- Add libraries without noting name + licence in docs/progress.md.
- Edit applied SQL scripts or scaffolded files. Commit secrets. Use any / bypassSecurityTrust.
