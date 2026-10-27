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
- Schema lives in `database/scripts/NNN_Description.sql`, applied in order. Never edit an applied script – add a new one.
- EF Core model is generated with **Scaffold-DbContext**; the exact command is in `database/scaffold-command.txt`.
  Entities -> `src/QaTracker.Domain/Entities`, DbContext -> `src/QaTracker.Infrastructure/Persistence/QaTrackerDbContext.cs`.
- Never hand-edit scaffolded files; extend via `partial` classes / `OnModelCreatingPartial` in separate files.
- Connection string: `ConnectionStrings:QaTracker` in `src/QaTracker.Api/appsettings.json` (local `.\SQLEXPRESS`, Windows auth).
- No EF migrations.

## Backend
- .NET 10, Clean layers (Domain, Application, Infrastructure, Api). Feature folders. No MediatR/AutoMapper.
- Async + CancellationToken. Result<T> + ProblemDetails. DTO records. AsNoTracking projections.
- FluentValidation, Serilog, IClock, ICurrentUser. UTC datetimeoffset.
- Central package versions in Directory.Packages.props; warnings are errors.
- Tests: xUnit v3 + Shouldly on Microsoft.Testing.Platform (`dotnet test`). Domain rules fully covered.

## Frontend (qa-tracker-web)
- Angular 22 standalone, zoneless, OnPush, signals, inject(), @if/@for/@defer.
- PrimeNG (QaPreset) + Tailwind 4. Tokens in `src/styles/tokens.css`, exposed as Tailwind classes in `src/styles.css`
  (`bg-surface`, `text-muted`, `text-severity-critical`, `rounded-card`…). No hard-coded colours.
- Dark mode = `app-dark` class on <html> (ThemeService); Tailwind `dark:` variant uses the same class.
- Every page: loading skeleton, empty state, error state. Dates dd-MMM-yyyy, IST.
- Charts only through shared ECharts wrappers; aggregation server-side.

## Commands
- Backend: `dotnet build` · `dotnet test` · `dotnet run --project src/QaTracker.Api` (http://localhost:5029, Scalar at /scalar)
- Frontend (in qa-tracker-web): `npm start` (http://localhost:4200) · `npm run build` · `npm test -- --watch=false` · `npm run lint` · `npm run format`
- DB: `sqlcmd -S .\SQLEXPRESS -E -i database\scripts\<file>.sql`, then the scaffold command.

## Never
- Add libraries without noting name + licence in docs/progress.md.
- Edit applied SQL scripts or scaffolded files. Commit secrets. Use any / bypassSecurityTrust.
