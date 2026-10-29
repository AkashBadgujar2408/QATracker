> Generated from QA_Tracker_Build_Plan.docx. Where this plan conflicts with docs/decisions.md (e.g. database-first, ADR-001), **decisions.md wins**.

**QA TRACKER**

Testing & Issue Coordination Software

Product specification and phase / session-wise build plan

.NET 10 Web API  ·  Angular 22  ·  SQL Server

**Prepared by:** Akash Badgujar

**Version:** 1.0 – 27 Oct 2026

**Intended reader:** Claude (AI coding assistant) and the development team

**Working budget:** ≈ 2 hours per day, one build session per day

**Contents**

# 1. How to use this document

This document is the single source of truth for building QA Tracker. It is written so that it can be handed to Claude one **build session** at a time. Each session is sized for roughly **2 hours** of focused work and ends with a working, committed increment.

## 1.1 Daily routine (2 hours)

| **Time** | **Activity** |
| --- | --- |
| 0:00 – 0:10 | Open the repo, start Claude, paste the session prompt (Appendix B). Claude reads `CLAUDE.md`, `docs/progress.md` and the session card. |
| 0:10 – 1:35 | Claude implements the session tasks. You review diffs, answer questions, run the app. |
| 1:35 – 1:50 | Run build + tests, click through the feature, fix anything broken. |
| 1:50 – 2:00 | Commit. Claude updates `docs/progress.md` (done / deviations / next). Stop – do not start the next session with leftover minutes. |

## 1.2 Ground rules for Claude

- Do **only** what the current session card lists. If something is missing from an earlier session, note it in `docs/progress.md` instead of silently expanding scope.
- Follow the coding standards (Section 13) and UI design system (Section 10) exactly – consistency matters more than cleverness.
- Every session must leave the solution **building, migrations applied and tests green**.
- If a session will clearly overrun, split it (e.g. S13a / S13b) and record the split in `docs/progress.md`.
- Never introduce a new library without listing it in the session notes with its licence.

## 1.3 Recommended repo files

- `CLAUDE.md` – the condensed rules from Appendix A (Claude Code reads this automatically).
- `docs/plan.md` – a Markdown copy of this document (Claude reads Markdown far more reliably than .docx).
- `docs/progress.md` – running log: one block per session (date, session id, done, deviations, next).
- `docs/decisions.md` – short architecture decision records when something deviates from this plan.

# 2. Product overview

## 2.1 The problem today

One tester tests several client projects and records issues in an Excel workbook (one tab per module, screenshots pasted on the sheet). The workbook is emailed to the developers, a developer fills in "Resolve By / Is Complete / Pending Reason / Valid-Invalid", and mails it back for retesting.

| **Observed in the current sheet** | **Consequence** |
| --- | --- |
| Only 1 of ~76 issues has a "Resolve By" name | Nobody knows who owns what; developers cannot pick work in parallel |
| Status columns mostly empty | Tester has to chase developers for status |
| Severity / priority written inside free text | No filtering, sorting or reporting |
| Numbering restarts per tab, no IDs, no dates | Issues cannot be referenced; no ageing or SLA |
| Screenshots float on the sheet ("img B", "img C") | Evidence is detached from the issue |
| No "fixed on which URL / please retest" step | Retesting is guess-work, reopen cycles are lost |
| One file edited and emailed by 8 people | Overwrites, version confusion, no history |

## 2.2 Goals

- Replace the Excel + email loop with one web application.
- Let **any developer** pick up or be assigned an issue, fix it and tell the tester on **which URL (UAT / LIVE)** it is deployed.
- Give the tester a fast, keyboard-friendly **Testing workspace** that matches how he works today, including Ctrl+V screenshots.
- Keep a complete, tamper-proof **history and chat** per issue ("Akash assigned PGERP-142 to Nitin").
- Organise work by **Client → Project → Testing Sprint**, with clear rules about when developers may start.
- Provide **multiple dashboards and charts** for progress, workload, quality and client health.

## 2.3 Out of scope (v1)

- Sprint planning / story points / time tracking.
- Client logins (internal tool only).
- Test-case management (possible v2).
- Mobile apps (the web UI is responsive to tablet width).

# 3. Glossary

| **Term** | **Meaning** |
| --- | --- |
| Client | A customer organisation, e.g. Pardi Gears. |
| Project | A software product for a client. Has a short unique **Key** (e.g. `PGERP`) used in issue IDs. |
| Module | A functional area of a project, e.g. Sales & Distribution, Kaizen. |
| Screen | Free-text name of the page where an issue occurs. **No master table** – suggestions are built from previous issues of the same module. |
| Environment | A URL where the project runs. Type is UAT, LIVE or Other. A project can have several. |
| Testing Sprint | A named testing round for one project, e.g. "Sprint 1 – Early Testing". Created by the tester from the Testing screen; may span several days. |
| Phase | The kind of testing round: Early Testing, UAT, LIVE, Regression, Other (editable list). |
| Issue | A defect or observation. ID = project key + running number, e.g. `PGERP-142`. |
| Deployment | A developer's record that a fix is live on a given environment URL. Creates a pending retest. |
| Retest | The tester's check of a deployment: Passed or Failed. |
| Activity timeline | The per-issue stream of comments and system events (chat + history). |

# 4. Roles and permissions

There are exactly three roles. The system is designed for any number of testers even though there is one today.

| **Action** | **Tester** | **Developer** | **Admin** |
| --- | --- | --- | --- |
| Add / edit clients, projects, modules, environments (incl. quick-add on Testing screen) | ✔ | View | ✔ |
| Start a Testing Sprint, add / edit / delete issues while the sprint is in testing | ✔ | ✖ | ✔ |
| Mark sprint "Testing Completed" | ✔ | ✖ | ✔ |
| Re-open a completed sprint for testing (override, logged) | ✖ | ✖ | ✔ |
| Assign / reassign an issue to anyone | ✔ | ✔ (only after Testing Completed) | ✔ |
| Pick (self-assign), start, put on hold, ask for info | ✖ | ✔ (only after Testing Completed) | ✔ |
| Mark Fixed + record deployment URL(s) | ✖ | ✔ | ✔ |
| Dispute ("Not a bug") | ✖ | ✔ | ✔ |
| Verify / Reopen / Reject / Close an issue | ✔ | ✖ | ✔ |
| Comment, @mention, attach files | ✔ | ✔ | ✔ |
| View all dashboards | ✔ | ✔ | ✔ |
| Manage users, roles, SLA settings, phases | ✖ | ✖ | ✔ |

**Note:** Developers can always **view** issues of a sprint that is still in testing (read-only, with a "Testing in progress" banner) so they can prepare, but every action button is disabled until the tester marks the sprint Testing Completed.

# 5. Domain model and hierarchy

```
Client
 └─ Project  (Key: PGERP)
     ├─ Modules            Sales & Distribution, Kaizen, WDC ...
     ├─ Environments       UAT 1 – https://uat.client.com   (UAT)
     │                     LIVE  – https://erp.client.com   (LIVE)
     ├─ Members            developers usually working on it
     └─ Testing Sprints
          ├─ Sprint 1 – Early Testing   started 22-Oct-2026   45 issues   [Closed]
          └─ Sprint 1 – UAT             started 27-Oct-2026   74 issues   [Testing Completed]
                └─ Issues  PGERP-46 ... PGERP-119
                      ├─ Activity timeline (comments + events)
                      ├─ Attachments
                      └─ Deployments (UAT 1 ✔ passed, LIVE ⏳ pending)
```

- Nothing is hard-deleted except issues of a sprint still in testing. Masters are **deactivated** so history stays intact.
- Module names are unique within a project; project keys are unique globally; environment names unique within a project.
- Issue numbers are per project and never reused.

# 6. Testing Sprint lifecycle

A sprint is **not** planned in advance. The tester opens the Testing screen, selects client and project, and either continues an open sprint or starts a new one inline. He can keep adding issues to the same sprint over several days until he marks it complete.

```
 TESTING  (tester adds issues, developers view only)
    │   Tester: "Mark Testing Completed"        ▲ Admin override: "Reopen testing" (logged)
    ▼                                           │
 TESTING COMPLETED  (no new issues; developers pick, fix, deploy; tester retests)
    │   Tester: "Close sprint"  (all issues Closed/Rejected or carried forward)
    ▼
 CLOSED  (read-only archive, snapshot frozen for reports)
```

| **State** | **Tester can** | **Developers can** | **System behaviour** |
| --- | --- | --- | --- |
| **Testing** | Add, edit, delete issues; pre-assign issues; add attachments; quick-add masters | View read-only only | No notifications to developers yet. Sprint shows "Day 1, Day 2…" counters based on dates issues were logged. |
| **Testing Completed** | Cannot add issues or edit issue details. Can comment, Verify, Reopen, Reject, Close, reassign | Pick, assign, start, need-info, hold, dispute, mark Fixed, add deployments, comment | On transition: issues get due dates from SLA, assignees are notified, unassigned issues appear in "Available to pick". Records TestingCompletedAt/By. |
| **Closed** | Read-only (comments still allowed) | Read-only (comments still allowed) | Allowed only when every issue is Closed or Rejected, or remaining ones were carried forward to another sprint. Freezes a snapshot for reports. |

## 6.1 Rules

- A project can have **at most one sprint in Testing state** at a time (keeps the Testing screen unambiguous). Several sprints can be in Testing Completed simultaneously.
- Starting a sprint asks only for: **Name** (suggested, e.g. "Sprint 2 – UAT"), **Phase**, **Default environment**. Start date = now.
- Marking Testing Completed shows a confirmation summary (count by module and severity, unassigned count, issues with "Not assessed" severity as a warning) and cannot be undone except by Admin.
- New defects found while retesting a completed sprint go into a **new sprint** (the Testing screen offers "Start Sprint 1 – UAT Round 2" pre-filled).
- **Carry forward** (Phase 6): when closing, open issues can be moved to another sprint of the same project; the issue keeps its ID and the timeline records the move.
- Every state change is written to the sprint's own history (who, when, note).

# 7. Issue specification

## 7.1 Fields

| **Group** | **Field** | **Rules** |
| --- | --- | --- |
| Identity | Key, Title | Key auto (`PGERP-142`). Title required, max 200. |
| Where | Client, Project, Sprint (auto), Module, Screen, Found on environment, Page URL | Module required. Screen free text with suggestions (max 150). Environment defaults from sprint. |
| What | Type, Steps to reproduce, Expected result, Actual result, Impact | Type: Bug, Validation, UI, Security, Performance, Enhancement, Clarification. Actual result required. Rich text, sanitised. |
| How bad | Severity, Priority | Severity: Critical, High, Medium, Low, Not assessed. Priority: High, Medium, Low. Both required. |
| Who | Reported by, Assigned to, Watchers | Reporter = current user. Assignee optional. |
| State | Status, Hold reason, Reject type + reason, Reopen count | See 7.2. |
| When | Created, Due date, First fixed at, Verified at, Closed at | Due date = TestingCompletedAt + SLA(severity); editable by tester. |
| Evidence | Attachments | Images (paste / drop), PDF, XLSX, DOCX, TXT. Max 10 MB each. |

## 7.2 Issue status lifecycle

```
                       ┌────────── NEED INFO ◄──────────┐
                       ▼                                │
 NEW ──► ASSIGNED ──► IN PROGRESS ──► FIXED ──► VERIFIED ──► CLOSED
  │         ▲   │          │           │  ▲          │            │
  │         │   │          │           │  └─ add LIVE deployment ─┘ (optional)
  │         │   ├─► ON HOLD (reason)   │
  │         │   └─► DISPUTED ──► REJECTED (tester agrees)
  │         │                    └──► ASSIGNED (tester disagrees)
  │         └──── REOPENED ◄─────────┘ (retest failed)  ◄── also from CLOSED if it regresses
  └─(sprint in Testing: tester may simply delete)
```

| **From → To** | **Who** | **Sprint must be** | **Required input** |
| --- | --- | --- | --- |
| New → Assigned / reassign | Tester, Dev, Admin | Tester: any · Dev: Testing Completed | Assignee, optional note |
| New/Assigned/Reopened → In Progress (Pick / Start) | Developer | Testing Completed | Auto-assigns to self if unassigned (atomic) |
| In Progress → Fixed | Assignee developer | Testing Completed | Fix note, ≥1 environment, optional version/commit |
| Assigned/In Progress → Need Info | Developer | Testing Completed | Question (becomes a comment, mentions reporter) |
| Need Info → In Progress | Developer (after reply) | Testing Completed | – |
| Assigned/In Progress → On Hold | Developer, Tester | Testing Completed | Reason |
| On Hold → Assigned | Developer, Tester | Testing Completed | – |
| Assigned/In Progress → Disputed | Developer | Testing Completed | Reason |
| Disputed → Rejected | Tester | Testing Completed | Reject type: Invalid / Duplicate (link) / As designed / Won't fix |
| Disputed → Assigned | Tester | Testing Completed | Comment |
| Fixed → Verified | Tester | Testing Completed | Retest passed on the deployment's environment |
| Fixed → Reopened | Tester | Testing Completed | Reason (+ screenshot recommended). ReopenCount + 1 |
| Verified → Fixed | Developer | Testing Completed | New deployment (e.g. LIVE) → new pending retest |
| Verified → Closed | Tester | Testing Completed | If project "Require LIVE verification" is on, a passed LIVE retest is needed |
| Closed → Reopened | Tester | Testing Completed | Reason |

**Implementation:** All transition rules live in **one** domain class (`IssueWorkflow`) that takes (issue, sprint state, user role, user id, target status) and returns allowed / reason. The API and the UI both use it (the API exposes `allowedActions` per issue so the UI never duplicates the rules).

## 7.3 Assignment and parallel work

- **Tester assigns** any issue to any developer (also during Testing as a pre-assignment – the developer is notified only when testing completes).
- **Developers** can pick an unassigned issue, assign to themselves or reassign to someone else with an optional note.
- **Pick is atomic**: `UPDATE Issues SET AssigneeId=@me WHERE Id=@id AND AssigneeId IS NULL`; zero rows → "Already picked by Nitin".
- All edits use optimistic concurrency (`rowversion` → HTTP 409 with "changed by X, reload").
- Issue detail shows **other open issues on the same screen/module** so one developer can fix related issues together and avoid merge conflicts.
- Bulk assign from the issue list (select many → assign).
- Every assignment writes a timeline event: "Akash assigned PGERP-142 to Nitin", "Nitin reassigned to Rahul – BOM is Rahul's", "Rahul picked PGERP-157".

## 7.4 Deployments and retest (UAT / LIVE)

- Marking Fixed requires choosing one or more environment URLs of the project. Each creates a **Deployment** row with `RetestResult = Pending`.
- **Batch deploy notice**: a developer selects several of his Fixed issues, chooses an environment and adds one note → one notification to the tester, one timeline entry per issue.
- The issue header shows an environment strip, e.g. `UAT 1 ✔ Verified 28-Oct · LIVE ⏳ Awaiting retest`.
- Tester's **Retest Queue** groups pending deployments by environment URL (open the URL once, clear all).
- Retest Passed → Verified. Retest Failed → Reopened (with reason).

## 7.5 Activity timeline (chat + history)

One chronological stream per issue mixing comments and immutable system events. Real-time via SignalR so two people on the same issue see messages instantly.

```
🧪 Akash reported in "Sprint 1 – UAT" on UAT 1              27-Oct 10:15   [screenshot]
⚙  Akash assigned to Nitin                                   27-Oct 10:16
⚙  Sprint marked Testing Completed – due 30-Oct (High)       27-Oct 18:00
💬 Nitin: Should zero be allowed or only ≥ 1? @Akash          28-Oct 11:30   (Need Info)
💬 Akash: Zero is allowed.                                   28-Oct 11:45
⚙  Nitin marked Fixed · deployed on UAT 1 – "min=0 on UI+API"  28-Oct 18:10
❌ Akash: Retest failed on UAT 1 – still accepts -5 on paste    29-Oct 11:00   Reopened (1)
⚙  Nitin marked Fixed · deployed on UAT 1                     29-Oct 16:20
✅ Akash verified on UAT 1                                    29-Oct 17:05
```

- Comments support @mentions, attachments and Ctrl+V images; editable by the author for 10 minutes, never deletable by others.
- System events cannot be edited or deleted (audit trail).
- Filter toggle: All · Comments only · History only.

# 8. Feature specifications

## 8.1 Testing workspace (tester's main screen)

The screen the tester lives in. Layout: **context bar** on top, **quick issue form** on the left (≈ 40%), **this sprint's issue list** on the right (≈ 60%).

- **Context bar**: Client ▸ Project ▸ Sprint selectors (remembered per user). If the project has no sprint in Testing, the Sprint selector shows "+ Start new sprint" (inline dialog: name, phase, default environment). Shows sprint stats chips: total, today, by severity, "Day 3".
- Every selector (Client, Project, Module, Environment) is a searchable dropdown with **"+ Add new"** – typing a non-existent name offers "Add 'Sales Return' as new module". Quick-add dialogs ask only essential fields and show "similar existing" names to prevent duplicates.
- **Quick issue form**: Module, Screen (suggestions), Type, Severity, Priority, Title, Steps, Expected, Actual, Impact, Assignee (optional), attachments. Ctrl+V anywhere on the form pastes a screenshot. **Save & add another** (Ctrl+Enter) keeps Module/Screen/Type.
- Right panel: issues of the current sprint (newest first) with inline edit, duplicate (clone) and delete while in Testing. Grouped-by-module toggle.
- "**Mark Testing Completed**" button with confirmation summary.

## 8.2 Other screens

| **Screen** | **Primary user** | **Purpose / key elements** |
| --- | --- | --- |
| Login | All | Email + password, "remember me". Forced password change on first login. |
| Home / Overview dashboard | All | KPI cards + charts (Section 9.1). Role-aware shortcuts. |
| Testing workspace | Tester | Section 8.1. |
| Sprints | All | List per project with status badge, progress bar, counts; open sprint dashboard; Mark Testing Completed / Close. |
| Issue list | All | Data table with filters (client, project, sprint, module, environment, status, severity, priority, assignee, overdue, text), saved views, column chooser, bulk assign, Excel export. |
| Issue board | Developers | Kanban by status, filters by project/sprint/assignee, drag = transition (validated by `allowedActions`). |
| Issue detail | All | Header (key, title, status, severity, priority, assignee, due), environment strip, action buttons from `allowedActions`, details panel, attachments gallery, related issues, activity timeline + composer. |
| My work | Developer | Tabs: Assigned to me · Available to pick · Need info answered · Reopened · Overdue. |
| Retest queue | Tester | Pending deployments grouped by environment URL, one-click Pass / Fail. |
| Deploy notice | Developer | Select own Fixed issues → environment → note → notify. |
| Dashboards | All | Sprint, Developer, Quality, Client health (Section 9). |
| Masters | Tester, Admin | Tree: Clients → Projects → Modules / Environments / Members. Activate / deactivate. |
| Import from Excel | Tester, Admin | Upload legacy workbook → choose client/project/sprint → preview grid → commit (Appendix C). |
| Notifications | All | Bell dropdown + full page; mark read; per-type email preferences. |
| Users & settings | Admin | Users, roles, SLA hours per severity, sprint phases, SMTP settings. |

## 8.3 Notifications

| **Event** | **Recipients** | **In-app** | **Email** |
| --- | --- | --- | --- |
| Sprint marked Testing Completed | All project members + pre-assigned devs | ✔ | ✔ (one summary) |
| Issue assigned to you | Assignee | ✔ | ✔ |
| @mention | Mentioned user | ✔ | ✔ |
| Marked Fixed / deploy notice | Reporter (tester) | ✔ | ✔ (one per batch) |
| Reopened | Assignee | ✔ | ✔ |
| Need info / disputed | Reporter | ✔ | ✔ |
| Comment on issue you report, own or watch | Them | ✔ | Daily digest |
| Critical issue in a completed sprint unassigned > 2 h | All project members | ✔ | ✔ |
| Overdue | Assignee | ✔ | Daily digest |

Default SLA (editable by Admin): Critical 1 working day, High 3, Medium 7, Low 15. Working days exclude Sundays and configurable holidays.

## 8.4 Import and export

- **Excel import** of the legacy workbook format (Appendix C) into a chosen client, project and sprint.
- **Issue list export** to Excel with current filters.
- **Sprint report** export to Excel and PDF (summary, by module, by severity, full issue list with latest status and screenshots thumbnails) – replaces the sheet currently emailed to clients.

# 9. Dashboards and analytics

All dashboards share a **global filter bar**: Client, Project, Sprint, Date range (default last 30 days). Filters are kept in the URL so views can be bookmarked and shared. Every chart has a title, a one-line subtitle explaining what it shows, a hover tooltip, an empty state, and **click-through** to the filtered issue list.

## 9.1 Overview (home)

| **Widget** | **Type** | **Definition** |
| --- | --- | --- |
| Open issues | KPI card + trend vs last week | Status in New, Assigned, In Progress, Need Info, On Hold, Disputed, Reopened |
| Awaiting retest | KPI card | Deployments with RetestResult = Pending |
| Overdue | KPI card (red when > 0) | Open and DueDate < now |
| Critical open | KPI card | Open and Severity = Critical |
| Closed this week | KPI card | ClosedAt within current week |
| Issues by status | Donut | Count per status group |
| Open by severity | Horizontal bar | Fixed severity colours |
| Created vs closed | Line (2 series), daily | Inflow vs outflow over date range |
| Open by project | Bar | Top 10 projects |
| Recent activity | Feed | Last 20 timeline events across visible projects |

## 9.2 Sprint dashboard

| **Widget** | **Type** | **Definition** |
| --- | --- | --- |
| Sprint header | Card | Name, phase, status, started, testing days, testing completed date |
| Progress | 100% stacked bar | Open / Fixed / Verified / Closed / Rejected |
| Burn-down | Line | Open count per day since Testing Completed, with ideal line to latest due date |
| Issues found per testing day | Column | Count by CreatedAt date while in Testing |
| Module × severity | Heatmap | Where the problems concentrate |
| Issues by type | Donut | Bug, Validation, UI, Security… |
| Reopen count | KPI + list | Issues reopened ≥ 1, sorted by count |
| Sprint comparison | Grouped column | Issues per sprint of the project (e.g. Early 45 → UAT 74 → LIVE 8) by severity |

## 9.3 Developer dashboard

| **Widget** | **Type** | **Definition** |
| --- | --- | --- |
| Workload | Stacked horizontal bar per developer | Assigned / In Progress / Need Info / On Hold |
| Fixed per week | Line per developer | First Fixed event per issue, weekly |
| Average fix time | Bar per developer | First FixedAt − max(TestingCompletedAt, first AssignedAt), working hours |
| First-time-right rate | Bar per developer | 1 − (issues reopened ≥ 1 / issues fixed). Framed as quality signal, shown as % |
| SLA compliance | Gauge | Fixed on or before due / total fixed |
| My stats (developer view) | Cards | My open, my overdue, fixed this week, my avg fix time |

## 9.4 Quality and testing dashboard

| **Widget** | **Type** | **Definition** |
| --- | --- | --- |
| Defect leakage | Stacked column per project | Issues found by phase (Early / UAT / LIVE). Healthy = most found early |
| Retest turnaround | Bar + KPI | RetestedAt − DeployedAt (median, working hours) |
| Retest queue ageing | Column buckets | < 1 day, 1–2, 3–5, > 5 days |
| Module hotspots | Treemap | Issues per module across all sprints of a project |
| Rejected ratio | KPI + donut by reject type | Invalid / Duplicate / As designed / Won't fix |
| Severity trend | Stacked area, weekly | New issues per week by severity |

## 9.5 Client health dashboard

| **Widget** | **Type** | **Definition** |
| --- | --- | --- |
| Client cards | Card grid | Per client: open, critical, overdue, last sprint status, health badge (green/amber/red) |
| Open by client & severity | Stacked bar |  |
| UAT vs LIVE issues | Grouped column per project | By FoundOnEnvironment type |
| Average closure time per client | Bar | CreatedAt → ClosedAt (working days) |

## 9.6 Implementation rules for charts

- Library: **Apache ECharts via ngx-echarts**, wrapped in our own components (`app-kpi-card`, `app-chart-bar`, `app-chart-donut`, `app-chart-line`, `app-chart-heatmap`, `app-chart-treemap`) so every chart looks identical.
- Fixed semantic colours (Section 10.2) for severity and status in every chart; never rely on colour alone – labels or legends always present.
- All aggregation is done **server-side** in dedicated analytics endpoints returning chart-ready DTOs; the UI never aggregates raw issue lists.
- Charts respect light / dark theme and resize responsively; skeleton loader while loading.

# 10. UI / UX design system

## 10.1 Principles

- **Calm and dense**: business tool used all day – compact tables, generous white space around groups, no decorative clutter.
- **Consistency over novelty**: one way to show status, one way to confirm, one way to show errors.
- **Keyboard first for the tester**: N = new issue, Ctrl+Enter = save, Ctrl+V = paste screenshot, / = search, Esc = close dialog.
- **Always show where you are**: breadcrumb Client ▸ Project ▸ Sprint on every scoped page.
- Accessible: WCAG 2.1 AA contrast, visible focus rings, labels on all inputs.

## 10.2 Tokens

| **Token** | **Value** | **Use** |
| --- | --- | --- |
| Font | Inter (fallback: Segoe UI, system-ui) | All text; tabular numbers in tables and KPIs |
| Type scale | 12 / 13 / 14 (body) / 16 / 20 / 24 / 30 px | Captions → page titles |
| Spacing | 4 px grid: 4, 8, 12, 16, 24, 32, 48 | Padding, gaps |
| Radius | 6 px controls, 10 px cards, 999 px badges |  |
| Primary | Indigo #4F46E5 (dark: #818CF8) | Primary buttons, links, active nav |
| Neutral | Slate scale #0F172A … #F8FAFC | Text, borders, surfaces |
| Severity | Critical #DC2626 · High #EA580C · Medium #D97706 · Low #2563EB · Not assessed #64748B | Badges, charts |
| Status | New #64748B · Assigned #6366F1 · In Progress #0EA5E9 · Need Info #A855F7 · On Hold #F59E0B · Disputed #EC4899 · Fixed #14B8A6 · Verified #22C55E · Closed #15803D · Reopened #EF4444 · Rejected #94A3B8 | Status pills, board columns, charts |
| Sprint state | Testing #0EA5E9 · Testing Completed #6366F1 · Closed #64748B | Sprint badges |
| Elevation | Card: 0 1px 2px rgb(0 0 0 / .06); Dialog: 0 10px 30px rgb(0 0 0 / .15) |  |

## 10.3 Layout and components

- **Shell**: collapsible left sidebar (icons + labels) · top bar with global search (issue key or text), project switcher, theme toggle, notification bell, user menu.
- Navigation: Home · Testing (tester) · My work (developer) · Issues · Board · Retest queue · Sprints · Dashboards ▸ · Masters · Admin.
- Page template: title + breadcrumb + primary action on the right; filters row; content.
- Components from **PrimeNG** themed with our tokens; utilities via **Tailwind CSS**. No other UI kits.
- Shared components: `status-pill`, `severity-badge`, `priority-icon`, `user-avatar`, `issue-key-link`, `entity-picker` (searchable + quick-add), `paste-dropzone`, `rich-text-editor`, `timeline`, `empty-state`, `confirm-dialog`, `page-header`, `filter-bar`.
- Feedback: toasts for success (3 s), inline field errors, ProblemDetails messages shown verbatim when user-safe, skeleton loaders (never blank screens), optimistic UI for comments.
- Dates shown as **dd-MMM-yyyy** (27-Oct-2026) and times in IST; relative time ("2 h ago") with exact time on hover.
- Responsive down to 1024 px (tablet); tables scroll horizontally inside their card, never the page.
- Light and dark themes from day one (tokens as CSS variables).

# 11. Technical architecture

## 11.1 Technology stack

| **Area** | **Choice** | **Notes** |
| --- | --- | --- |
| Runtime | .NET 10 (LTS), C# 14, ASP.NET Core 10 | Nullable enabled, TreatWarningsAsErrors |
| Data | EF Core 10 + SQL Server 2022 (Express acceptable) | Code-first migrations |
| Auth | ASP.NET Core Identity + JWT access token (15 min) + refresh token in httpOnly cookie | Role + policy based |
| Real time | ASP.NET Core SignalR | Groups per user, per issue, per project |
| Validation | FluentValidation | Apache-2.0 |
| Mapping | Riok.Mapperly (source generator) or manual | Avoid AutoMapper/MediatR (now commercial licences) |
| Logging | Serilog → console + rolling file | Request logging, correlation id |
| Excel | ClosedXML | Import + export, MIT |
| PDF | QuestPDF | Community licence – confirm eligibility |
| HTML sanitising | HtmlSanitizer (Ganss.Xss) | All rich-text fields |
| API docs | Built-in OpenAPI + Scalar UI | Dev only |
| Backend tests | xUnit, Shouldly, Testcontainers (SQL Server), WebApplicationFactory | Avoid FluentAssertions v8+ (commercial) |
| Frontend | Angular 22 (standalone, signals, zoneless), TypeScript strict | Angular CLI |
| UI kit | PrimeNG (version matching Angular 22) + Tailwind CSS 4 | Themed via tokens |
| Charts | Apache ECharts + ngx-echarts | Wrapped components |
| Rich text | TipTap (via ngx-tiptap) or Quill | Must support image paste |
| Frontend tests | Vitest (Angular default) + Playwright for a few E2E flows |  |
| Hosting | Single server: IIS or Docker (API + static Angular) + SQL Server | HTTPS, nightly DB + attachment backup |

**Versions:** Library versions change; at session S01 Claude must check the current compatible versions (PrimeNG for Angular 22, ngx-echarts, etc.) and pin them in package.json / Directory.Packages.props.

## 11.2 Backend solution structure

```
QaTracker.sln
├─ src/
│  ├─ QaTracker.Domain/           entities, enums, IssueWorkflow, SprintRules, errors
│  ├─ QaTracker.Application/      feature folders (use cases), DTOs, validators,
│  │                              interfaces: IFileStorage, IEmailSender, ICurrentUser, IClock
│  │     └─ Features/Issues/ReportIssue/ {Command, Handler, Validator, Dto}.cs
│  ├─ QaTracker.Infrastructure/   EF Core (DbContext, configurations, migrations), Identity,
│  │                              file storage, SMTP, ClosedXML, QuestPDF, background jobs
│  └─ QaTracker.Api/              Minimal API groups per feature, auth, SignalR hub,
│                                 ProblemDetails, OpenAPI
├─ tests/
│  ├─ QaTracker.Domain.Tests/              IssueWorkflow + SprintRules matrices
│  ├─ QaTracker.Application.Tests/
│  └─ QaTracker.Api.IntegrationTests/      Testcontainers SQL Server
├─ Directory.Build.props · Directory.Packages.props · .editorconfig · global.json
└─ docker-compose.yml   (SQL Server for local dev)
```

Dependencies point inwards: Api → Application → Domain; Infrastructure → Application. Handlers are plain classes registered in DI (no mediator library); each endpoint calls one handler.

## 11.3 Frontend structure

```
qa-tracker-web/src/app/
├─ core/        auth (service, interceptor, guards), typed API clients, realtime (SignalR),
│               notifications, layout (shell, sidebar, topbar), error handling, theme
├─ shared/      ui (status-pill, severity-badge, entity-picker, paste-dropzone, timeline),
│               charts/*, pipes (relativeTime, istDate), directives (hasRole), models
├─ features/
│   ├─ testing-workspace/     ├─ issues/ (list, detail, board)
│   ├─ my-work/               ├─ retest-queue/
│   ├─ sprints/               ├─ dashboards/ (overview, sprint, developer, quality, client)
│   ├─ masters/               ├─ import/
│   └─ admin/
└─ app.routes.ts   (lazy loadChildren per feature, role guards)
```

- State: signals in feature-level stores (`@Injectable` services exposing `signal`/`computed`); no NgRx.
- HTTP: typed services per feature; API base URL from environment config; interceptor adds JWT, handles 401 refresh and maps ProblemDetails to toasts.
- Real time: one `RealtimeService` owning the SignalR connection; features subscribe to typed events and patch their signals.

# 12. Data model

| **Table** | **Key columns** |
| --- | --- |
| Users (Identity) | Id, FullName, Email, Role (Tester/Developer/Admin), IsActive, MustChangePassword, AvatarColor |
| Clients | Id, Name (unique), ContactPerson, Email, Phone, IsActive, audit cols |
| Projects | Id, ClientId, Name, Key (unique, 2–8 upper-case), NextIssueNumber, RequireLiveVerification, IsActive |
| ProjectMembers | ProjectId, UserId |
| Modules | Id, ProjectId, Name, IsActive · unique (ProjectId, Name) |
| Environments | Id, ProjectId, Name, Type (UAT/LIVE/Other), Url, IsActive |
| SprintPhases | Id, Name, SortOrder, IsActive (seed: Early Testing, UAT, LIVE, Regression, Other) |
| TestingSprints | Id, ProjectId, Name, PhaseId, DefaultEnvironmentId, Status (Testing/TestingCompleted/Closed), StartedAt, StartedById, TestingCompletedAt, TestingCompletedById, ClosedAt, ClosedById, Notes, RowVersion · filtered unique index: one Testing sprint per project |
| SprintEvents | Id, SprintId, UserId, Kind (Started/TestingCompleted/ReopenedForTesting/Closed), Note, At |
| Issues | Id, ProjectId, Number, Key (computed), SprintId, CarriedFromSprintId, ModuleId, ScreenName, FoundOnEnvironmentId, PageUrl, Type, Title, Steps, ExpectedResult, ActualResult, Impact, Severity, Priority, Status, HoldReason, RejectType, RejectReason, DuplicateOfIssueId, ReporterId, AssigneeId, DueDate, ReopenCount, CreatedAt, UpdatedAt, FirstFixedAt, VerifiedAt, ClosedAt, RowVersion |
| IssueActivities | Id, IssueId, UserId, Kind (Created/Comment/StatusChanged/Assigned/FieldChanged/Deployed/RetestPassed/RetestFailed/Attachment/CarriedForward/SprintEvent), Body, OldValue, NewValue, EnvironmentId, DeploymentId, CreatedAt, EditedAt |
| Mentions | ActivityId, UserId |
| Attachments | Id, IssueId, ActivityId, FileName, ContentType, SizeBytes, StoragePath, ThumbnailPath, UploadedById, UploadedAt |
| Deployments | Id, IssueId, EnvironmentId, DeployNoticeId, DeployedById, DeployedAt, Note, Version, RetestResult (Pending/Passed/Failed), RetestedById, RetestedAt, RetestComment |
| DeployNotices | Id, ProjectId, EnvironmentId, ById, At, Note |
| IssueWatchers | IssueId, UserId |
| IssueLinks | FromIssueId, ToIssueId, Type (Duplicate/Related) |
| Notifications | Id, UserId, Type, IssueId, SprintId, Title, Body, IsRead, CreatedAt |
| NotificationPreferences | UserId, Type, Email (bool) |
| SlaPolicies | Id, Severity, WorkingHours |
| Holidays | Date, Name |
| SprintSnapshots | SprintId, IssueId, StatusAtClose, SeverityAtClose, AssigneeAtClose |
| SavedViews | Id, UserId, Name, Page, FilterJson |
| EmailOutbox | Id, To, Subject, Body, CreatedAt, SentAt, Attempts, LastError |

- All timestamps `datetimeoffset` stored in UTC; UI converts to IST.
- Indexes: Issues(ProjectId, Status), Issues(SprintId, Status), Issues(AssigneeId, Status), Issues(ModuleId, ScreenName), Deployments(RetestResult, EnvironmentId), IssueActivities(IssueId, CreatedAt).
- Issue numbering: increment `Projects.NextIssueNumber` inside the same transaction with `UPDLOCK` to avoid duplicates.

# 13. Coding standards

## 13.1 Backend (.NET)

- Feature-folder (vertical slice) organisation inside Application; one handler per use case; endpoints thin (bind → call handler → map result).
- Async all the way with `CancellationToken` on every I/O method. No `.Result` / `.Wait()`.
- Errors: domain/application return a `Result<T>` with typed errors; API maps to **RFC 9457 ProblemDetails** (400 validation, 403 forbidden action, 404, 409 concurrency/conflict, 422 workflow rule violation).
- Authorisation in two layers: endpoint policies (role) + workflow checks (state/ownership) in the domain.
- EF Core: `AsNoTracking()` + projection to DTO for reads; no lazy loading; configurations in `IEntityTypeConfiguration<T>` classes; migrations named `YYYYMMDD_Description`.
- Never expose entities from the API; DTOs are records.
- Time via `IClock` (testable); current user via `ICurrentUser`.
- Naming: PascalCase types/methods, camelCase locals, `_camelCase` private fields, `Async` suffix, plural table names.
- Analyzers on, warnings as errors, `dotnet format` clean. Central package management (`Directory.Packages.props`).
- Logging: structured (`{IssueKey}`), never log secrets or full request bodies.
- Configuration via options classes validated on start (`ValidateOnStart`). Secrets in user-secrets / environment variables, never in git.

## 13.2 Frontend (Angular)

- Standalone components only, `ChangeDetectionStrategy.OnPush`, zoneless; `inject()` instead of constructor injection.
- Built-in control flow (`@if`, `@for` with `track`, `@defer` for heavy charts).
- Signals for state (`signal`, `computed`, `input()`, `output()`, `model()`); RxJS only for HTTP/streams, converted with `toSignal` at the edge.
- Typed reactive forms; validation messages from a single shared helper.
- Smart (page) vs presentational (shared UI) component split; presentational components have no services.
- TypeScript `strict`, no `any`; ESLint (angular-eslint) + Prettier; file names kebab-case; one component per file.
- All colours, spacing and radii from CSS variables / Tailwind theme – no hard-coded hex values in components.
- Every list page: loading skeleton, empty state, error state.

## 13.3 Database and Git

- Migrations reviewed in every session; never edit an applied migration.
- Seed data via a dedicated seeder (admin user, phases, SLA defaults) that is idempotent.
- Git: `main` always deployable; one branch per session `s07-sprint-api`; Conventional Commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`); squash-merge after the session.

## 13.4 Definition of done (every session)

- Builds with zero warnings; all tests pass; new logic has tests (domain rules 100% of transitions).
- Feature manually verified in the browser in light and dark theme.
- No TODOs without an entry in `docs/progress.md`.
- API changes visible in Scalar; UI uses only shared components and tokens.
- `docs/progress.md` updated; changes committed.

# 14. API catalogue

```
AUTH        POST /api/auth/login · /api/auth/refresh · /api/auth/logout · GET /api/auth/me ·
            POST /api/auth/change-password
USERS       GET/POST/PUT /api/users · POST /api/users/{id}/reset-password ·
            PATCH /api/users/{id}/active
CLIENTS     GET/POST/PUT /api/clients · PATCH /api/clients/{id}/active
PROJECTS    GET/POST/PUT /api/clients/{clientId}/projects · GET /api/projects/{id}
            GET/POST/PUT /api/projects/{id}/modules | /environments | /members
LOOKUPS     GET /api/lookups/cascade?clientId=&projectId=   (Testing screen dropdowns)
            GET /api/modules/{id}/screen-suggestions?q=
SPRINTS     GET  /api/projects/{id}/sprints · POST /api/projects/{id}/sprints (start)
            GET  /api/sprints/{id} · POST /api/sprints/{id}/complete-testing ·
              POST /api/sprints/{id}/close
            POST /api/sprints/{id}/reopen-testing (admin) ·
              POST /api/sprints/{id}/carry-forward {issueIds, targetSprintId}
            GET  /api/sprints/{id}/report?format=xlsx|pdf
ISSUES      GET  /api/issues?clientId&projectId&sprintId&moduleId&envId&status&severity
                 &priority&assigneeId&overdue&q&page&size&sort
            POST /api/issues · GET /api/issues/{key} (includes allowedActions) ·
              PUT /api/issues/{key} (If-Match) · DELETE /api/issues/{key}
            POST /api/issues/{key}/assign {userId, note} · POST /api/issues/{key}/pick
            POST /api/issues/{key}/transition {action, reason, rejectType, duplicateOfKey}
            POST /api/issues/{key}/fixed {note, environmentIds[], version}
            POST /api/deployments/{id}/retest {passed, comment}
            POST /api/issues/bulk-assign {keys[], userId}
            GET  /api/issues/{key}/activities · POST /api/issues/{key}/comments ·
              PUT /api/comments/{id}
            POST /api/issues/{key}/attachments · GET /api/attachments/{id} ·
              GET /api/attachments/{id}/thumbnail
            GET  /api/issues/{key}/related
WORK        GET /api/me/work · GET /api/retest-queue?projectId= ·
            POST /api/projects/{id}/deploy-notices
ANALYTICS   GET /api/analytics/overview · /sprint/{id} · /developers · /quality ·
            /clients   (all accept common filters)
IMPORT      POST /api/import/excel/preview (multipart) · POST /api/import/excel/commit
EXPORT      GET /api/issues/export?{same filters}
NOTIFY      GET /api/notifications · POST /api/notifications/read ·
            GET/PUT /api/notifications/preferences
ADMIN       GET/PUT /api/settings/sla · GET/POST/PUT /api/settings/phases ·
            GET/POST /api/settings/holidays
SIGNALR     /hubs/tracker  →  IssueChanged, ActivityAdded, NotificationCreated, SprintChanged
```

# 15. Testing, security and non-functional requirements

## 15.1 Testing strategy

- **Domain unit tests**: full matrix for `IssueWorkflow` (every status × action × role × sprint state) and `SprintRules`.
- **Integration tests** (Testcontainers SQL Server): auth, issue numbering under concurrency, atomic pick race (two parallel requests → one 200, one 409), optimistic concurrency, sprint gating (developer action on Testing sprint → 422), attachment authorisation.
- **Frontend**: Vitest for shared components and stores; Playwright E2E for 3 flows – tester logs issue with paste, developer picks & fixes, tester retests.

## 15.2 Security

- HTTPS only; HSTS; secure, httpOnly, SameSite=Strict refresh cookie; rate-limit login.
- All rich text sanitised server-side on write; Angular binds with built-in sanitisation; no `bypassSecurityTrust*`.
- Uploads: extension + magic-byte check, size limit, stored outside web root with random names, served via authorised endpoint with `Content-Disposition`.
- Every query filtered by authorisation; no IDOR (issue keys resolved server-side with permission checks).
- Audit: timeline events and sprint events are append-only.

## 15.3 Non-functional

| **Area** | **Target** |
| --- | --- |
| Users | ≈ 10 concurrent, design for 50 |
| Performance | Issue list (1 000 rows filtered) < 500 ms API; dashboard endpoints < 800 ms |
| Availability | Office hours; nightly backups (DB full + attachments), 14-day retention |
| Browser support | Latest Chrome, Edge, Firefox |
| Localisation | English UI, IST time zone, dd-MMM-yyyy |

# 16. Build plan – phases and 2-hour sessions

The build is split into **8 phases** and **32 sessions**. Each session is designed for about 2 hours with Claude. At one session per working day:

| **Pace** | **Calendar time (incl. ~20% buffer for overruns and review days)** |
| --- | --- |
| 5 sessions / week | ≈ 8 weeks |
| 6 sessions / week | ≈ 6.5 weeks |

## 16.1 Milestones

| **Milestone** | **After session** | **What you can do** |
| --- | --- | --- |
| M1 – Tester can log issues | S11 | Tester stops using Excel for new issues; developers view them |
| M2 – Full fix / retest loop | S17 | Email loop retired: assign, pick, fix with URL, retest, timeline. **Go live internally here.** |
| M3 – Real time + notifications + board | S20 | Live updates, bell, emails, Kanban |
| M4 – Dashboards | S24 | All five dashboards |
| M5 – Reports & import | S27 | Sprint report Excel/PDF, legacy Excel import |
| M6 – Production ready | S32 | SLA, carry forward, hardening, deployment |

## 16.2 Session overview

| **#** | **Phase** | **Session** | **Depends on** |
| --- | --- | --- | --- |
| S01 | P0 Foundation | Solution & workspace scaffold | – |
| S02 | P0 Foundation | App shell & shared UI primitives | S01 |
| S03 | P0 Foundation | Domain entities & database | S01 |
| S04 | P0 Foundation | Authentication API | S03 |
| S05 | P0 Foundation | Authentication UI & users admin | S02, S04 |
| S06 | P1 Masters | Masters API | S04 |
| S07 | P1 Masters | Masters UI + entity-picker | S05, S06 |
| S08 | P2 Sprints & reporting | Testing Sprint API | S06 |
| S09 | P2 Sprints & reporting | Issue API – create, edit, list | S08 |
| S10 | P2 Sprints & reporting | Attachments | S09 |
| S11 | P2 Sprints & reporting | Testing workspace UI | S07, S09, S10 |
| S12 | P3 Workflow | IssueWorkflow domain | S09 |
| S13 | P3 Workflow | Workflow API | S12 |
| S14 | P3 Workflow | Timeline & comments API | S13 |
| S15 | P3 Workflow | Issue list & detail UI | S11, S14 |
| S16 | P3 Workflow | My work, retest queue & deploy notice API | S13 |
| S17 | P3 Workflow | My work, retest queue & deploy notice UI | S15, S16 |
| S18 | P4 Real time | SignalR & in-app notifications | S17 |
| S19 | P4 Real time | Email notifications | S18 |
| S20 | P4 Real time | Kanban board | S15, S18 |
| S21 | P5 Dashboards | Analytics API | S13 |
| S22 | P5 Dashboards | Chart components & Overview dashboard | S21 |
| S23 | P5 Dashboards | Sprint & Developer dashboards | S22 |
| S24 | P5 Dashboards | Quality & Client health dashboards | S22 |
| S25 | P6 Reports & import | Sprint report export | S21 |
| S26 | P6 Reports & import | Excel import API | S09, S10 |
| S27 | P6 Reports & import | Import UI | S26 |
| S27b | P6 Reports & import | Import end-to-end check (carry-over of S27) | S27 |
| S28 | P7 Hardening | SLA, overdue & settings | S19 |
| S29 | P7 Hardening | Sprint close & carry forward | S08, S13 |
| S30 | P7 Hardening | Security & integration test pass | S29 |
| S31 | P7 Hardening | UX polish & performance | S30 |
| S32 | P7 Hardening | Deployment & handover | S31 |

## 16.3 Session cards

**Phase 0 – Foundation (S01–S05)**

### S01 · Solution & workspace scaffold

**Goal:** Create backend solution and Angular workspace with all tooling so later sessions only add features. · **Depends on:** –

**Tasks**

- Create .NET 10 solution with the 4 src + 3 test projects (11.2), Directory.Build.props (nullable, warnings as errors, analyzers), Directory.Packages.props, .editorconfig, global.json.
- API: Serilog, ProblemDetails, OpenAPI + Scalar, CORS for Angular dev, health endpoint `/health`, docker-compose with SQL Server.
- Angular 22 workspace `qa-tracker-web`: strict TS, zoneless, ESLint + Prettier, Tailwind 4, PrimeNG themed preset, Inter font, CSS variable tokens (10.2) for light/dark.
- Check and pin current compatible library versions; record them in `docs/progress.md`.
- Create `CLAUDE.md` (Appendix A), `docs/progress.md`, `docs/decisions.md`.

**Done when**

- `dotnet build` and `ng build` clean; `/health` returns 200; Angular shows a blank themed page with dark-mode toggle.

### S02 · App shell & shared UI primitives

**Goal:** Build the layout every page will use and the first shared components. · **Depends on:** S01

**Tasks**

- Shell: sidebar (collapsible), top bar (search box placeholder, theme toggle, bell placeholder, user menu), router outlet, breadcrumb service.
- Shared: `page-header`, `empty-state`, `status-pill`, `severity-badge`, `priority-icon`, `user-avatar`, `confirm-dialog` service, toast service, skeleton helpers.
- A hidden `/dev/ui` route showcasing every shared component in both themes (living style guide).

**Done when**

- Style guide page renders all components correctly in light and dark; no hard-coded colours.

### S03 · Domain entities & database

**Goal:** Create all entities, enums, EF configurations, first migration and seeder. · **Depends on:** S01

**Tasks**

- Entities and enums for every table in Section 12 (TestingSprints, Issues, IssueActivities, Deployments, etc.).
- EF configurations, indexes, filtered unique index (one Testing sprint per project), rowversion columns, computed Key.
- Idempotent seeder: admin user, phases, SLA defaults.
- Initial migration; apply on startup in Development only.

**Done when**

- Database created from migration; seeder runs twice without duplicates; a test verifies the model builds.

### S04 · Authentication API

**Goal:** Secure login with three roles. · **Depends on:** S03

**Tasks**

- Identity with custom user; login, refresh (httpOnly cookie), logout, me, change-password endpoints.
- Policies: TesterOrAdmin, DeveloperOrAdmin, AdminOnly, Authenticated; `ICurrentUser`.
- Users admin endpoints (list, create with temp password, edit, deactivate, reset password).
- Integration tests for login, refresh, role-protected endpoint.

**Done when**

- Can log in via Scalar; protected endpoints return 401/403 correctly; tests green.

### S05 · Authentication UI & users admin

**Goal:** Login screen and admin user management in the app. · **Depends on:** S02, S04

**Tasks**

- Login page (design system), forced password change, auth store (signals), JWT interceptor with silent refresh, route guards, `*hasRole` directive.
- Admin › Users page: table, create/edit dialog, activate/deactivate, reset password.

**Done when**

- Log in as admin, create a tester and developer, log in as each; sidebar items differ by role.

**Phase 1 – Masters (S06–S07)**

### S06 · Masters API

**Goal:** Clients, projects, modules, environments, members. · **Depends on:** S04

**Tasks**

- CRUD + activate/deactivate endpoints with validation and uniqueness rules; project key validation.
- Cascade lookup endpoint for dropdowns; quick-add variants returning the created item; "similar names" check endpoint.
- Tests for uniqueness and authorisation (developer cannot create).

**Done when**

- All masters manageable via Scalar; tests green.

### S07 · Masters UI + entity-picker

**Goal:** Masters tree page and the reusable searchable dropdown with "+ Add new". · **Depends on:** S05, S06

**Tasks**

- Masters page: tree (Client → Project → Modules / Environments / Members) with detail panels and forms.
- Shared `entity-picker` component: search, keyboard navigation, "+ Add 'typed text'" option, opens a quick-add dialog with similar-name warning, emits created item.
- Quick-add dialogs for client, project (auto-suggest key), module, environment.

**Done when**

- Tester can build the Pardi Gears hierarchy entirely from pickers; duplicates are warned.

**Phase 2 – Sprints & issue reporting (S08–S11)**

### S08 · Testing Sprint API

**Goal:** Inline sprint start and lifecycle rules. · **Depends on:** S06

**Tasks**

- Domain `SprintRules`; endpoints: list per project, start (name suggestion), get, complete-testing (sets due dates from SLA, writes events, queues notifications placeholder), admin reopen-testing.
- Unit tests for every sprint transition and the one-Testing-sprint rule.

**Done when**

- Starting a second Testing sprint for the same project fails with a clear 422; tests green.

### S09 · Issue API – create, edit, list

**Goal:** Core issue endpoints for the tester. · **Depends on:** S08

**Tasks**

- Create (numbering with lock), update (If-Match rowversion), delete (only while sprint in Testing), get by key, list with all filters, paging and sorting.
- Screen-suggestion endpoint; HTML sanitising; `Created` activity written automatically.
- Tests: concurrent creates get unique numbers; edit blocked after Testing Completed for tester; 409 on stale rowversion.

**Done when**

- Issues can be created and filtered via Scalar; tests green.

### S10 · Attachments

**Goal:** Upload, thumbnail and secure download. · **Depends on:** S09

**Tasks**

- `IFileStorage` (local folder), upload endpoint with type/magic-byte/size checks, thumbnail generation for images, authorised download and thumbnail endpoints.
- Shared Angular `paste-dropzone` (Ctrl+V, drag-drop, file picker, previews, remove) and `attachment-gallery` with lightbox.

**Done when**

- Pasting a screenshot uploads it and shows a thumbnail; unauthorised download returns 403.

### S11 · Testing workspace UI

**Goal:** The tester's main screen (8.1). · **Depends on:** S07, S09, S10

**Tasks**

- Context bar with Client/Project/Sprint pickers (remembered), "+ Start new sprint" dialog, sprint stat chips.
- Quick issue form with rich-text fields, screen suggestions, paste screenshots, Save & add another (Ctrl+Enter), N shortcut.
- Right panel: this sprint's issues with edit / clone / delete, group-by-module.
- "Mark Testing Completed" with confirmation summary.

**Done when**

- Tester can log 10 issues with screenshots in a few minutes without touching the mouse much. **Milestone M1.**

**Phase 3 – Workflow & collaboration (S12–S17)**

### S12 · IssueWorkflow domain

**Goal:** All issue transition rules in one tested class. · **Depends on:** S09

**Tasks**

- Implement `IssueWorkflow` (7.2 table) returning allowed actions and violations; `allowedActions` projection for a user.
- Exhaustive parameterised unit tests (status × action × role × sprint state).

**Done when**

- 100% transition coverage; tests green.

### S13 · Workflow API

**Goal:** Assign, pick, transitions, fixed with deployments, retest. · **Depends on:** S12

**Tasks**

- Endpoints: assign, atomic pick, transition (need info, hold, dispute, reject, reopen, close, start), fixed {environments}, retest, bulk assign.
- Every action writes the right IssueActivities rows (assignment text "X assigned to Y").
- Integration tests: pick race, developer blocked while sprint in Testing, reopen increments count.

**Done when**

- Full loop possible via Scalar; tests green.

### S14 · Timeline & comments API

**Goal:** Chat + history per issue. · **Depends on:** S13

**Tasks**

- Activities endpoint (paged, newest last), comment create/edit (10-minute window), mentions parsing, attachments on comments, watchers auto-add (reporter, assignee, commenters).
- Related issues endpoint (same module + screen, open).

**Done when**

- Timeline returns comments and events in order with user names; tests green.

### S15 · Issue list & detail UI

**Goal:** Browse and act on issues. · **Depends on:** S11, S14

**Tasks**

- Issue list: filter bar, data table, saved views (local first), bulk assign, row click → detail.
- Issue detail: header, environment strip, action buttons from `allowedActions` with the right dialogs (fixed → environment checkboxes, reject → type, reopen → reason + paste).
- Timeline component with composer (@mention autocomplete, paste), filters All/Comments/History.

**Done when**

- Developer can pick and fix an issue; tester can verify/reopen; every step visible in the timeline.

### S16 · My work, retest queue & deploy notice API

**Goal:** Work queues and batch deployment. · **Depends on:** S13

**Tasks**

- `/me/work` (tabs data), `/retest-queue` grouped by environment, deploy-notice endpoint (validates issues are caller's Fixed ones).
- Tests for grouping and batch behaviour.

**Done when**

- Endpoints return correct groups; tests green.

### S17 · My work, retest queue & deploy notice UI

**Goal:** Daily screens for developers and tester. · **Depends on:** S15, S16

**Tasks**

- My Work page with tabs and counts; Available-to-pick with Pick button.
- Retest Queue grouped by environment with open-URL button and one-click Pass / Fail (fail asks reason + screenshot).
- Deploy Notice page.

**Done when**

- End-to-end: tester logs → completes testing → dev picks → fixes on UAT → tester retests. **Milestone M2 – go live internally.**

**Phase 4 – Real time, notifications, board (S18–S20)**

### S18 · SignalR & in-app notifications

**Goal:** Live updates and the bell. · **Depends on:** S17

**Tasks**

- Hub with user/issue/project groups; publish IssueChanged, ActivityAdded, SprintChanged, NotificationCreated from handlers (after commit).
- Notifications table + endpoints; notification rules from 8.3.
- Angular RealtimeService, bell dropdown with unread count, live timeline and list updates.

**Done when**

- Two browsers: comment in one appears instantly in the other; bell updates live.

### S19 · Email notifications

**Goal:** Outbox-based email with preferences. · **Depends on:** S18

**Tasks**

- EmailOutbox + background sender with retry; SMTP options; simple branded HTML templates.
- Preferences page per notification type; testing-completed summary email; daily digest job (comments, overdue).

**Done when**

- Emails delivered to a dev SMTP catcher (e.g. smtp4dev); preferences respected.

### S20 · Kanban board

**Goal:** Drag-and-drop board for developers. · **Depends on:** S15, S18

**Tasks**

- Board by status columns, filters, cards (key, title, severity, assignee avatar, due), drag uses `allowedActions` and opens required-input dialogs; live updates.

**Done when**

- Invalid drops are refused with a message; valid drops persist and appear live elsewhere. **Milestone M3.**

**Phase 5 – Dashboards & analytics (S21–S24)**

### S21 · Analytics API

**Goal:** Server-side aggregates for all dashboards. · **Depends on:** S13

**Tasks**

- Endpoints for overview, sprint, developers, quality, clients with common filter parameters; chart-ready DTOs.
- Working-hours calculator (SLA, fix time) using holidays; efficient SQL (projections, grouping in DB).
- Tests with a seeded dataset verifying key numbers.

**Done when**

- Each endpoint < 800 ms on 5 000 issues seed; tests green.

### S22 · Chart components & Overview dashboard

**Goal:** Reusable chart kit and the home page. · **Depends on:** S21

**Tasks**

- ECharts wrappers: kpi-card, bar, stacked bar, donut, line, heatmap, treemap, gauge – themed with tokens, light/dark, responsive, empty and loading states, click-through event.
- Global dashboard filter bar synced to URL.
- Overview dashboard (9.1).

**Done when**

- Home page shows all widgets; clicking a chart segment opens the filtered issue list.

### S23 · Sprint & Developer dashboards

**Goal:** Sections 9.2 and 9.3. · **Depends on:** S22

**Tasks**

- Sprint dashboard incl. burn-down and sprint comparison; linked from Sprints page.
- Developer dashboard with team view and personal "My stats".

**Done when**

- Numbers match the issue list for the same filters.

### S24 · Quality & Client health dashboards

**Goal:** Sections 9.4 and 9.5. · **Depends on:** S22

**Tasks**

- Quality dashboard (leakage, retest turnaround, ageing, hotspots, rejected ratio, severity trend).
- Client health dashboard with health badge rules (red if critical open or overdue > 3).

**Done when**

- All five dashboards complete. **Milestone M4.**

**Phase 6 – Reports & import (S25–S27)**

### S25 · Sprint report export

**Goal:** Excel and PDF sprint reports. · **Depends on:** S21

**Tasks**

- ClosedXML report: summary sheet + one sheet per module mirroring the old workbook layout, with status columns filled.
- QuestPDF report: cover, summary charts (rendered tables), issue list with thumbnails.
- Issue list export with current filters.

**Done when**

- Downloaded reports open cleanly in Excel and a PDF viewer.

### S26 · Excel import API

**Goal:** Import the legacy workbook (Appendix C). · **Depends on:** S09, S10

**Tasks**

- Parser: tabs → modules, "N. Title" blocks, Actual/Expected/Impact, Severity|Priority split, images anchored to rows → attachments, Resolve By → assignee.
- Preview endpoint returning parsed rows + warnings; commit endpoint (transactional).
- Test with an anonymised copy of the real workbook.

**Done when**

- Real workbook parses with ≥ 95% rows needing no manual correction.

### S27 · Import UI

**Goal:** Upload, review, commit. · **Depends on:** S26

**Tasks**

- Wizard: upload → choose/quick-add client, project, sprint → editable preview grid with warnings → commit → summary with links.

**Done when**

- Legacy workbook imported into a test project end-to-end. **Milestone M5.**

### S27b · Import end-to-end check (carry-over of S27)

**Goal:** Prove the import wizard end-to-end and close Milestone M5. · **Depends on:** S27

**Tasks**

- Start API + web; as a tester in Chrome run `/import` with the sample workbook
  (`dotnet run tests/fixtures/legacy-workbook/make-sample-workbook.cs -- "<path>.xlsx"`): upload → client/project
  (quick-add a test project) → start a sprint if needed → review (fix a warning, skip a row, rename a module) →
  import → check the summary links, the issue list, statuses, screenshots and comments. Check dark theme and phone
  width (grid scrolls inside its card, no page scroll).
- Run the preview on the anonymised real "Pardi Gears Testing Sheet" if it is available; tune the parser for the
  ≥ 95 % rows-without-correction target (S26 done-when) and add it as a test fixture.
- Fix what the check finds; update progress.md.

**Done when**

- A legacy workbook is imported into a test project end-to-end in the browser. **Milestone M5.**

**Phase 7 – Hardening & go-live (S28–S32)**

### S28 · SLA, overdue & settings

**Goal:** SLA engine and admin settings. · **Depends on:** S19

**Tasks**

- Settings pages: SLA hours, holidays, phases. SMTP test already exists (Admin › E-mail, S19); add **editing the SMTP
  settings in the app** (today appsettings + user secrets, ADR-029).
- Hourly job (a BackgroundService next to the S19 workers, last run in `JobRuns`): creates the in-app **Overdue**
  notification for the assignee (once per issue and due date) and the **CriticalUnassigned** alert for all project
  members (critical issue in a completed sprint unassigned > 2 h, once per issue). Both go through the ChangeFeed, so
  CriticalUnassigned is also mailed instantly and Overdue stays in the daily digest (S19). Overdue badges in
  list/board/detail.

**Done when**

- Changing SLA affects new due dates; overdue and critical-unassigned notifications fire once each in a test; SMTP
  settings changed in the app are used by the next test mail.

### S29 · Sprint close & carry forward

**Goal:** Finish the sprint lifecycle. · **Depends on:** S08, S13

**Tasks**

- Close sprint with validation; carry-forward dialog (select open issues → target sprint); snapshots on close; admin reopen-testing UI.
- Timeline/sprint events for every step.

**Done when**

- Closed sprint report still shows numbers as of closing after issues move on.

### S30 · Security & integration test pass

**Goal:** Close gaps before production. · **Depends on:** S29

**Tasks**

- Review against 15.2: rate limiting, headers, upload checks, IDOR tests for every endpoint group.
- Malformed JSON bodies (non-UTF-8, invalid enum value) answer **400 ProblemDetails, not 500** (open since S08).
- Playwright E2E for the 3 critical flows.

**Done when**

- All security checks pass; E2E green.

### S31 · UX polish & performance

**Goal:** Make it feel finished. · **Depends on:** S30

**Tasks**

- Consistency sweep against design system; empty/loading/error states everywhere; keyboard shortcuts help dialog (?).
- Performance: query review, indexes, `@defer` for charts, bundle size check.

**Done when**

- Lighthouse accessibility ≥ 90 on main pages; issue list < 500 ms.

### S32 · Deployment & handover

**Goal:** Run it on the office/cloud server. · **Depends on:** S31

**Tasks**

- Production configuration, HTTPS, IIS or Docker deployment scripts, DB migration step, backup jobs (DB + attachments), README with ops steps.
- Import the current Pardi Gears workbook into production; create real users.

**Done when**

- Team uses the system; Excel retired. **Milestone M6.**

# Appendix A – CLAUDE.md for the repository

Put this file at the repository root. It condenses the rules Claude must follow in every session.

```
# QA Tracker – rules for Claude
 
## Context
- Testing & issue coordination app. Roles: Tester, Developer, Admin.
- Hierarchy: Client > Project (Key) > Module; Environments (UAT/LIVE URLs); Testing Sprints.
- Full spec: docs/plan.md. Progress log: docs/progress.md. Decisions: docs/decisions.md.
 
## Session discipline
- Work ONLY on the session named in the prompt. ~2 hours. Stop when the card is done.
- Start: read this file, docs/progress.md, and the session card in docs/plan.md.
- End: build + tests green, update docs/progress.md (done / deviations / next), commit.
 
## Key business rules
- Sprint states: Testing -> TestingCompleted -> Closed. One Testing sprint per project.
- Only tester/admin add or edit issues, and only while sprint is Testing.
- Developers act on issues only when sprint is TestingCompleted.
- All issue transitions go through Domain/IssueWorkflow. UI uses allowedActions from API.
- Every state/assignment change writes an IssueActivities row. Activities are append-only.
- Pick is atomic. All updates use rowversion (409 on conflict).
 
## Backend
- .NET 10, Clean layers (Domain, Application, Infrastructure,
  Api). Feature folders. No MediatR/AutoMapper.
- Async + CancellationToken. Result<T> + ProblemDetails. DTO records. AsNoTracking projections.
- FluentValidation, Serilog, IClock, ICurrentUser. UTC datetimeoffset.
- Tests: xUnit + Shouldly + Testcontainers. Domain rules fully covered.
 
## Frontend
- Angular 22 standalone, zoneless, OnPush, signals, inject(), @if/@for/@defer.
- PrimeNG + Tailwind. Use shared components and CSS variable tokens only. No hard-coded colours.
- Every page: loading skeleton, empty state, error state. Dates dd-MMM-yyyy, IST.
- Charts only through shared ECharts wrappers; aggregation server-side.
 
## Never
- Add libraries without noting name + licence in docs/progress.md.
- Edit applied migrations. Commit secrets. Use any / bypassSecurityTrust.
```

# Appendix B – Session prompt template

Paste this at the start of each 2-hour session (replace the session id):

```
We are building QA Tracker. Today is session S07 – Masters UI + entity-picker.
 
1. Read CLAUDE.md, docs/progress.md and the S07 card in docs/plan.md (section 16.3).
2. Summarise in 5 lines what you will build and any open question. Wait for my OK.
3. Implement the tasks in small steps, building and testing as you go.
4. When "Done when" is met: run all tests, show me how to verify it manually,
   update docs/progress.md, and prepare a Conventional Commit message.
Do not work on anything outside S07. If something is missing from earlier sessions,
list it under "Carry-over" in docs/progress.md instead of building it now.
```

Template for `docs/progress.md` entries:

```
## S07 – Masters UI + entity-picker   (28-Oct-2026)
Done: ...
Deviations from plan: ...
Carry-over / known gaps: ...
Libraries added (licence): ...
Next session: S08
```

# Appendix C – Legacy Excel import mapping

Based on the current "Pardi Gears Testing Sheet" workbook (5 tabs, ~76 issues, ~90 screenshots).

| **Workbook element** | **Maps to** | **Parsing rule** |
| --- | --- | --- |
| Sheet tab (e.g. "Sales & Distribution") | Module | Create if missing (tab names with commas such as "Item Mast, BOM, Tax Str.Mast" kept as one module; user may rename in preview) |
| Column B text starting with "N. " | New issue; Title | Regex `^\d+\.\s*(.+)` on the first line; title = first line without number |
| "Actual Result:" text | ActualResult | Inline after label or in following row(s) |
| "Expected Result:" text | ExpectedResult | Same |
| "Issue:" text | Steps / description | Same |
| "Impact:" text | Impact | Same |
| "Severity: X<br>Priority: Y" | Severity, Priority | "Not yet assessed" / "Pending…" → Not assessed / Medium with warning |
| Floating images | Attachments | Anchor row between this issue's start row and the next issue's start row |
| "img B / img C" labels | – | Ignored (used only to order images) |
| Resolve By | Assignee | Match user by first name (case-insensitive); unmatched → warning |
| Is Complete = Yes | Status Verified | Otherwise New/Assigned |
| Pending Reason | On Hold + reason | Only if filled |
| Valid/Invalid = Invalid | Rejected (Invalid) | Only if filled |
| Text in other columns (e.g. "For your refer.") | Comment on the issue | Attributed to importing user |
