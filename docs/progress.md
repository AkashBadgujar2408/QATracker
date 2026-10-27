# QA Tracker – progress log

Plan: 32 sessions (S01–S32), ~2 h each. Milestones: M1 after S11 · M2 after S17 (go live internally) · M3 S20 · M4 S24 · M5 S27 · M6 S32.

| Session | Title | Status |
| --- | --- | --- |
| S01 | Solution & workspace scaffold | ✅ Done (27-Oct-2026) |
| S02 | App shell & shared UI primitives | ⏭ Next |
| S03 | Domain entities & database (database-first) | |
| S04 | Authentication API | |
| S05 | Authentication UI & users admin | |
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
| primeng | 22.1.4 | MIT |
| @primeuix/themes | 3.1.0 | MIT |
| tailwindcss / @tailwindcss/postcss | 4.3.3 | MIT |
| @fontsource-variable/inter | 5.3.0 | OFL-1.1 |
| angular-eslint / eslint / typescript-eslint | 22.6.0 / 10.13.0 / 8.71.0 | MIT |
| prettier | 3.9.10 | MIT |
| typescript | 6.0.3 | Apache-2.0 |
| vitest | 4.1.12 | MIT |

Checked for later sessions: FluentValidation 12.2.0 (Apache-2.0), ngx-echarts 22.0.0 + echarts 6.1.0 (MIT / Apache-2.0).

**Next session:** S02 – App shell & shared UI primitives.
