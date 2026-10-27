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
