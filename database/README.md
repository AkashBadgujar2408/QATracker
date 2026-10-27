# Database (database-first)

- `scripts/` – schema scripts, numbered `NNN_Description.sql`, run in order. Never edit a script that has
  already been applied to a shared database; add a new one instead.
- `scaffold-command.txt` – the `Scaffold-DbContext` / `dotnet ef dbcontext scaffold` command that regenerates
  the EF Core entities (`QaTracker.Domain/Entities`) and `QaTrackerDbContext` (`QaTracker.Infrastructure/Persistence`).

Local dev server: `.\SQLEXPRESS` (Windows authentication). Connection string: `src/QaTracker.Api/appsettings.json`.
