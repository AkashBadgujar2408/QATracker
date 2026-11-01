<#
.SYNOPSIS
  Full backup of the QA Tracker database and attachment files (plan 15.4: nightly, 14-day retention).

.DESCRIPTION
  Registered by install-server.ps1 as the "QA Tracker nightly backup" task (runs as SYSTEM); deploy.ps1 calls it with
  -Tag predeploy before every deploy. Writes
    <Root>\backups\db\<Database>_<tag>_yyyyMMdd_HHmmss.bak      (BACKUP DATABASE ... WITH CHECKSUM, then VERIFYONLY)
    <Root>\backups\files\attachments_<tag>_yyyyMMdd_HHmmss.tar.gz
  deletes backups older than the retention and, when install.json has BackupCopyTo (e.g. \\nas\qa-tracker), copies
  both files there too (keep a copy off this server). Exit code 1 on any failure (visible in Task Scheduler).
#>
param(
    [string] $Root = 'C:\QaTracker',
    [string] $Tag = 'nightly'
)

. (Join-Path $PSScriptRoot 'ops-common.ps1')

try {
    $config = Read-InstallConfig $Root
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $dbFolder = Join-Path $Root 'backups\db'
    $filesFolder = Join-Path $Root 'backups\files'
    New-Item -ItemType Directory -Force -Path $dbFolder, $filesFolder | Out-Null

    $bak = Join-Path $dbFolder "$($config.Database)_${Tag}_$stamp.bak"
    Write-OpsLog $Root 'backup' "Database -> $bak"
    Invoke-Sql $config.SqlServer 'master' "BACKUP DATABASE [$($config.Database)] TO DISK = N'$bak' WITH INIT, CHECKSUM, NAME = N'QA Tracker $Tag $stamp';" | Out-Null
    Invoke-Sql $config.SqlServer 'master' "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM;" | Out-Null

    $settings = Get-Content $config.SettingsFile -Raw | ConvertFrom-Json
    $attachments = $settings.Storage.Root
    $archive = Join-Path $filesFolder "attachments_${Tag}_$stamp.tar.gz"
    Write-OpsLog $Root 'backup' "Attachments $attachments -> $archive"
    New-Item -ItemType Directory -Force -Path $attachments | Out-Null
    # tar.exe ships with Windows 10 / Server 2019 and later.
    & "$env:windir\System32\tar.exe" -czf $archive -C (Split-Path $attachments -Parent) (Split-Path $attachments -Leaf)
    if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE." }

    if ($config.BackupCopyTo) {
        Write-OpsLog $Root 'backup' "Copy to $($config.BackupCopyTo)"
        New-Item -ItemType Directory -Force -Path $config.BackupCopyTo | Out-Null
        Copy-Item $bak, $archive $config.BackupCopyTo -Force
        Remove-OldFiles $config.BackupCopyTo $config.BackupRetentionDays | Out-Null
    }

    $removed = @(Remove-OldFiles $dbFolder $config.BackupRetentionDays) + @(Remove-OldFiles $filesFolder $config.BackupRetentionDays)
    Write-OpsLog $Root 'backup' ("Done ({0:N1} MB database, {1:N1} MB files, {2} old backup(s) removed)." -f `
        ((Get-Item $bak).Length / 1MB), ((Get-Item $archive).Length / 1MB), $removed.Count)
    exit 0
} catch {
    Write-OpsLog $Root 'backup' "FAILED: $($_.Exception.Message)"
    exit 1
}
