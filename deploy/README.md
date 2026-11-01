# QA Tracker – operations

How to run QA Tracker on a Windows server: IIS hosts the API and the Angular app as **one HTTPS site**, and SQL Server
(Express is enough) holds the data. The design is in docs/decisions.md ADR-042.

```
https://qa-tracker.example.local
        │  IIS site "QaTracker"  (app pool "QaTracker", No Managed Code, AlwaysRunning)
        ▼
C:\QaTracker\app\               QaTracker.Api.dll + wwwroot\ (Angular build)   ← replaced by every deploy
C:\QaTracker\config\            qa-tracker.settings.json (secrets), install.json, deployments.log
C:\QaTracker\data\attachments\  uploaded files                                  ← back up with the database
C:\QaTracker\logs\              qa-tracker-yyyyMMdd.log (30 days), deploy.log, backup.log, restore.log
C:\QaTracker\backups\           db\*.bak, files\*.tar.gz (14 days)
C:\QaTracker\ops\               backup.ps1, restore.ps1, … (copied by install / deploy)
C:\QaTracker\app-previous\      the app before the last deploy (rollback)
SQL Server .\SQLEXPRESS         database QaTracker (Windows login "IIS AppPool\QaTracker": read/write)
```

## 1. Server prerequisites (once)

| What | Notes |
| --- | --- |
| Windows Server 2019+ (or Windows 10/11 Pro for a trial) | `tar.exe` and IIS 10 come with it |
| SQL Server 2019+ (Express is fine) | Windows authentication; the installing admin must be sysadmin |
| SQL Server command line tools (`sqlcmd`) | `winget install Microsoft.Sqlcmd` or the ODBC + sqlcmd MSI |
| ASP.NET Core **10** Hosting Bundle | from https://dotnet.microsoft.com/download/dotnet/10.0, then `iisreset` |
| TLS certificate for the host name in `LocalMachine\My` | from the company CA or a public CA; note the thumbprint |
| DNS record for the host name → server | the install script adds a hosts-file entry on the server if missing |
| SMTP relay the server may use | entered later in the app (sidebar **E-mail**) |

IIS itself, WebSockets (SignalR), Application Initialization and HTTP redirection are installed by the script.

## 2. Build a release (developer machine)

```powershell
.\deploy\build-release.ps1          # runs dotnet test + ng test, then builds
# -> artifacts\release\qa-tracker-<yyyyMMdd>-<commit>.zip   (~40 MB)
```

The zip holds `app\` (win-x64 publish with the Angular build in `wwwroot`), `database\` (apply-scripts + SQL
scripts), `deploy\` (these scripts) and `version.txt`. Copy it to the server, then on the server:

```powershell
Unblock-File .\qa-tracker-*.zip      # files from another machine are blocked by Windows otherwise
Expand-Archive .\qa-tracker-20261101-abc1234.zip C:\Releases\qa-tracker-20261101-abc1234
cd C:\Releases\qa-tracker-20261101-abc1234
```

## 3. Install (once per server, elevated PowerShell)

```powershell
.\deploy\install-server.ps1 -HostName qa-tracker.example.local -CertificateThumbprint 'AB12…' `
    -AdminEmail admin@example.com [-BackupCopyTo \\nas\backups\qa-tracker] [-Root D:\QaTracker]
```

Without a certificate, `-SelfSignedCertificate` creates one (trial only – browsers warn until it is trusted).
The script prints the **temporary admin password once** – keep it for the first sign-in. It is safe to run again
(e.g. to change the certificate); an existing settings file is never overwritten.

`config\qa-tracker.settings.json` (readable by Administrators, SYSTEM and the app pool only) holds what differs per
server: connection string, JWT signing key (random), attachment and log paths, `Email:AppBaseUrl` (links in mails),
`Email:From`, the seed admin. Change it with Notepad as administrator and recycle the app pool. Everything else is in
`appsettings.json` / `appsettings.Production.json` inside the release.

## 4. Deploy (every release, elevated PowerShell)

```powershell
.\deploy\deploy.ps1 [-Root D:\QaTracker]
```

1. backup (database + attachments, tag `predeploy`), 2. maintenance page (`app_offline.htm`), 3. new database
scripts, 4. current app → `app-previous`, new app → `app`, 5. site back, smoke test `https://<host>/health`
(includes the database), 6. removes the temporary admin password from the settings file once the admin exists.
Takes 1–2 minutes; users see the maintenance page and the app reconnects by itself.

**After the first deploy:** sign in as `admin` with the temporary password → choose a new one.

## 5. Go-live checklist (S32 card: "Team uses the system; Excel retired")

1. Install + first deploy (above); sign in as admin, change the password.
2. Sidebar **E-mail**: SMTP host/port/TLS/account → *Send test mail*. Sidebar **Settings**: check holidays and SLA hours.
3. Sidebar **Users**: create every tester and developer (user name, e-mail, role); hand out the temporary
   passwords personally – everyone must change theirs at the first sign-in.
4. **Masters**: clients, projects (key), modules, environments (UAT / LIVE URLs) – or let the import create them.
5. **Import** the current workbook (sidebar **Import**): upload, check the preview and the people / masters it maps, import.
   Spot-check 10 issues against the sheet (status, assignee, dates, screenshots).
6. Next morning: check `logs\backup.log` and that `backups\db` has last night's `.bak` (Task Scheduler →
   "QA Tracker nightly backup" → Last run result 0x0).
7. Announce the URL; mark the Excel workbook read-only and archive it.

## 6. Rollback

* **App only** (no new SQL script in the release): run `deploy.ps1` from the previous release folder – or copy
  `C:\QaTracker\app-previous\*` back to `app\` and recycle the app pool.
* **The release added a SQL script**: scripts are forward-only. Restore the `predeploy` backup of that deploy
  (below), then deploy the previous release.

## 7. Backups and restore

Nightly at 01:30 (task "QA Tracker nightly backup", runs as SYSTEM): full database backup `WITH CHECKSUM` +
`RESTORE VERIFYONLY`, attachments as `.tar.gz`, retention 14 days, optional copy to `BackupCopyTo`
(**keep a copy off the server**). Run one now: `C:\QaTracker\ops\backup.ps1 -Tag manual`.

```powershell
# Restore drill (monthly): restores beside the live database and prints row counts – nothing live changes.
C:\QaTracker\ops\restore.ps1 -DatabaseBackup C:\QaTracker\backups\db\QaTracker_nightly_20261101_013000.bak -TargetDatabase QaTracker_Drill
sqlcmd -S .\SQLEXPRESS -E -Q "DROP DATABASE QaTracker_Drill"

# Real restore (elevated): takes the app offline, restores database + files of the same backup run, smoke test.
C:\QaTracker\ops\restore.ps1 -DatabaseBackup <...>.bak -AttachmentsArchive <...>.tar.gz
```

The current attachment folder is kept as `attachments.before-restore-<stamp>` – delete it once the restore is checked.

## 8. Day-to-day

| Task | How |
| --- | --- |
| Is it up? | `https://<host>/health` → `Healthy` (503 when SQL Server is down) |
| Logs | `C:\QaTracker\logs\qa-tracker-<date>.log` (search by time, user name or the request path the user reports) |
| App does not start (HTTP 500.30) | Windows Event Viewer → Application (source "IIS AspNetCore Module V2"); usually the settings file (JSON syntax, missing signing key) or SQL login |
| Mails not sent | sidebar **E-mail** → outbox / test mail |
| Locked-out user | sidebar **Users** → Reset password (lockout ends after 15 minutes anyway) |
| Renew the certificate | import the new one, then `install-server.ps1` again with the new thumbprint |
| Recycle | `Restart-WebAppPool QaTracker` (the pool also recycles daily at 03:30) |

Never put the settings file, backups or the release zip in source control.
