<#
.SYNOPSIS
  Deploys this release to a server prepared by install-server.ps1 (ADR-042).

.DESCRIPTION
  Run from the extracted release folder in an elevated Windows PowerShell:  .\deploy\deploy.ps1
    1. backup of database + attachments (ops\backup.ps1, tag "predeploy") - skip only with -SkipBackup,
    2. app offline (app_offline.htm: IIS stops the app and shows a maintenance page),
    3. database scripts (database\apply-scripts.ps1 - only the new ones run, forward only),
    4. previous app copied to app-previous, new app mirrored into app (data, logs and settings live elsewhere),
    5. app online, smoke test https://<host>/health (database included),
    6. the temporary admin password is removed from the settings file once the admin exists.
  On failure the app stays offline; see deploy\README.md "Rollback".
#>
param(
    [string] $Root = 'C:\QaTracker',
    [switch] $SkipBackup
)

. (Join-Path $PSScriptRoot 'ops-common.ps1')
Assert-Administrator
Import-Module WebAdministration

$config = Read-InstallConfig $Root
$release = Split-Path $PSScriptRoot -Parent
$source = Join-Path $release 'app'
$app = Join-Path $Root 'app'
$version = if (Test-Path (Join-Path $release 'version.txt')) { (Get-Content (Join-Path $release 'version.txt') -Raw).Trim() } else { 'unknown' }
if (-not (Test-Path (Join-Path $source 'QaTracker.Api.dll'))) { throw "$source does not contain a published QA Tracker - run this from an extracted release." }

Write-OpsLog $Root 'deploy' "Deploying $version"

if (-not $SkipBackup -and (Test-Path (Join-Path $app 'QaTracker.Api.dll'))) {
    & (Join-Path $PSScriptRoot 'backup.ps1') -Root $Root -Tag 'predeploy'
    if ($LASTEXITCODE -ne 0) { throw 'Backup failed - nothing deployed.' }
}

$offline = Join-Path $app 'app_offline.htm'
Copy-Item (Join-Path $PSScriptRoot 'app_offline.htm') $offline -Force
Start-Sleep -Seconds 5   # ANCM stops the app; open SignalR connections reconnect after the deploy.

& (Join-Path $release 'database\apply-scripts.ps1') -Server $config.SqlServer -Database $config.Database

$previous = Join-Path $Root 'app-previous'
& robocopy $app $previous /MIR /NFL /NDL /NJH /NJS /NP /XF app_offline.htm | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copy of the current app to $previous failed (robocopy $LASTEXITCODE)." }
& robocopy $source $app /MIR /NFL /NDL /NJH /NJS /NP /XF app_offline.htm | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copy of the new app failed (robocopy $LASTEXITCODE) - the app stays offline." }

Copy-Item (Join-Path $PSScriptRoot '*.ps1') (Join-Path $Root 'ops') -Force
Copy-Item (Join-Path $PSScriptRoot 'app_offline.htm') (Join-Path $Root 'ops') -Force

Remove-Item $offline -Force
if ((Get-WebAppPoolState -Name $config.SiteName).Value -ne 'Started') { Start-WebAppPool -Name $config.SiteName }
if ((Get-WebsiteState -Name $config.SiteName).Value -ne 'Started') { Start-Website -Name $config.SiteName }

if (-not (Test-AppHealth $config.HostName)) {
    Write-OpsLog $Root 'deploy' "FAILED $version - /health did not answer 200. See $(Join-Path $Root 'logs') and the Windows Application event log."
    throw 'Smoke test failed.'
}

# The seeder has created the admin at start-up (before /health answers): the temporary password is no longer needed.
$settings = Get-Content $config.SettingsFile -Raw | ConvertFrom-Json
if ($settings.Seed -and $settings.Seed.PSObject.Properties['AdminPassword']) {
    $userName = $settings.Seed.AdminUserName
    $admin = Invoke-Sql $config.SqlServer $config.Database "SELECT COUNT(*) FROM dbo.Users WHERE Role = N'Admin';"
    if ([int]($admin | Select-Object -First 1) -gt 0) {
        $settings.Seed.PSObject.Properties.Remove('AdminPassword')
        [IO.File]::WriteAllText($config.SettingsFile, ($settings | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
        Write-OpsLog $Root 'deploy' "Temporary password of '$userName' removed from the settings file."
    }
}

Add-Content (Join-Path $Root 'config\deployments.log') ('{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $version) -Encoding UTF8
Write-OpsLog $Root 'deploy' "Deployed $version - https://$($config.HostName)"
