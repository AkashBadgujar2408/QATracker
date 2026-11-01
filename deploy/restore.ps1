<#
.SYNOPSIS
  Restores the QA Tracker database and attachments from backup.ps1 files (disaster recovery, restore drill).

.DESCRIPTION
  Elevated Windows PowerShell on the server:
    .\restore.ps1 -DatabaseBackup C:\QaTracker\backups\db\QaTracker_nightly_20261101_013000.bak `
                  -AttachmentsArchive C:\QaTracker\backups\files\attachments_nightly_20261101_013000.tar.gz
  Takes the app offline, restores the database WITH REPLACE, keeps the current attachment folder as
  attachments.before-restore-<stamp> and unpacks the archive, then brings the app back and runs the smoke test.
  Use the two files of the same backup run (same stamp) so files and rows match.
  -TargetDatabase restores into another database instead (restore drill - the live one is not touched).
#>
param(
    [Parameter(Mandatory)] [string] $DatabaseBackup,
    [string] $AttachmentsArchive,
    [string] $TargetDatabase,
    [string] $Root = 'C:\QaTracker'
)

. (Join-Path $PSScriptRoot 'ops-common.ps1')

$config = Read-InstallConfig $Root
if (-not (Test-Path $DatabaseBackup)) { throw "$DatabaseBackup not found." }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'

if ($TargetDatabase) {
    # Drill: restore beside the live database (files renamed), check the row counts, leave it for inspection.
    $list = & sqlcmd -S $config.SqlServer -E -b -h -1 -W -s '|' -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$DatabaseBackup';"
    $dataDir = (Invoke-Sql $config.SqlServer 'master' "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400));" | Select-Object -First 1).Trim()
    $moves = $list | Where-Object { $_ -match '\|' } | ForEach-Object {
        $parts = $_.Split('|')
        $extension = if ($parts[2] -eq 'L') { '_log.ldf' } else { '.mdf' }
        "MOVE N'$($parts[0])' TO N'$(Join-Path $dataDir ($TargetDatabase + $extension))'"
    }
    Invoke-Sql $config.SqlServer 'master' "RESTORE DATABASE [$TargetDatabase] FROM DISK = N'$DatabaseBackup' WITH REPLACE, CHECKSUM, $($moves -join ', ');" | Out-Null
    $counts = Invoke-Sql $config.SqlServer $TargetDatabase "SELECT CONCAT((SELECT COUNT(*) FROM dbo.Issues), ' issues, ', (SELECT COUNT(*) FROM dbo.Users), ' users, ', (SELECT COUNT(*) FROM dbo.Attachments), ' attachments');"
    Write-OpsLog $Root 'restore' "Drill: $DatabaseBackup restored as [$TargetDatabase] - $($counts | Select-Object -First 1). Drop it when checked."
    return
}

# The live restore stops and starts the site: elevated PowerShell only (the drill above needs SQL rights only).
Assert-Administrator
Import-Module WebAdministration
$app = Join-Path $Root 'app'
$offline = Join-Path $app 'app_offline.htm'
Copy-Item (Join-Path $PSScriptRoot 'app_offline.htm') $offline -Force
Start-Sleep -Seconds 5

try {
    Write-OpsLog $Root 'restore' "Database [$($config.Database)] <- $DatabaseBackup"
    Invoke-Sql $config.SqlServer 'master' @"
IF DB_ID(N'$($config.Database)') IS NOT NULL ALTER DATABASE [$($config.Database)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [$($config.Database)] FROM DISK = N'$DatabaseBackup' WITH REPLACE, CHECKSUM;
ALTER DATABASE [$($config.Database)] SET MULTI_USER;
"@ | Out-Null

    if ($AttachmentsArchive) {
        $settings = Get-Content $config.SettingsFile -Raw | ConvertFrom-Json
        $attachments = $settings.Storage.Root
        if (Test-Path $attachments) {
            Rename-Item $attachments "$(Split-Path $attachments -Leaf).before-restore-$stamp"
        }
        Write-OpsLog $Root 'restore' "Attachments $attachments <- $AttachmentsArchive"
        & "$env:windir\System32\tar.exe" -xzf $AttachmentsArchive -C (Split-Path $attachments -Parent)
        if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE." }
    }
} finally {
    Remove-Item $offline -Force -ErrorAction SilentlyContinue
}

if (Test-AppHealth $config.HostName) {
    Write-OpsLog $Root 'restore' 'Restore finished - /health OK.'
} else {
    throw 'Restore finished but /health does not answer 200 - check the logs.'
}
