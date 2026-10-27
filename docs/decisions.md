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
  (plan 11.2). DTOs shared by a feature live in one file (`Features/Users/UserDto.cs`).
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
