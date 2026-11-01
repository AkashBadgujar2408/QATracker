<#
.SYNOPSIS
  One-time setup of a Windows server for QA Tracker (IIS + SQL Server, ADR-042). Safe to run again.

.DESCRIPTION
  Run from the extracted release folder in an elevated Windows PowerShell:
    .\deploy\install-server.ps1 -HostName qa-tracker.example.local -CertificateThumbprint <thumbprint> -AdminEmail admin@example.com

  It
    1. checks the prerequisites (IIS features, ASP.NET Core Hosting Bundle, sqlcmd) and installs missing IIS features,
    2. creates the folders under -Root (app, data\attachments, logs, backups, config, ops),
    3. writes config\qa-tracker.settings.json (connection string, random signing key, paths, temporary admin password)
       once - an existing file is never overwritten,
    4. creates the IIS app pool (no managed code, always running - the e-mail and SLA workers run inside it) and the
       HTTPS site, with QATRACKER_SETTINGS pointing at the settings file,
    5. creates / updates the database (database\apply-scripts.ps1) and gives the app pool read/write access,
    6. registers the nightly backup task (ops\backup.ps1, 14-day retention).
  Then run .\deploy\deploy.ps1 to copy the app and start it.

.PARAMETER CertificateThumbprint
  Certificate in Cert:\LocalMachine\My for -HostName. Without it, -SelfSignedCertificate creates one (test / intranet
  trial only - browsers warn until it is trusted).
#>
param(
    [Parameter(Mandatory)] [string] $HostName,
    [string] $CertificateThumbprint,
    [switch] $SelfSignedCertificate,
    [Parameter(Mandatory)] [string] $AdminEmail,
    [string] $MailFrom = "qa-tracker@$HostName",
    [string] $Root = 'C:\QaTracker',
    [string] $SiteName = 'QaTracker',
    [string] $SqlServer = '.\SQLEXPRESS',
    [string] $Database = 'QaTracker',
    [string] $BackupTime = '01:30',
    [int] $BackupRetentionDays = 14,
    [string] $BackupCopyTo
)

. (Join-Path $PSScriptRoot 'ops-common.ps1')
Assert-Administrator

$release = Split-Path $PSScriptRoot -Parent
$appPoolIdentity = "IIS AppPool\$SiteName"

Write-Host '== 1. Prerequisites'
if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
    # Windows Server
    $features = 'Web-Server', 'Web-WebSockets', 'Web-AppInit', 'Web-Http-Redirect', 'Web-Mgmt-Console'
    $missing = $features | Where-Object { -not (Get-WindowsFeature $_).Installed }
    if ($missing) { Install-WindowsFeature -Name $missing | Out-Null }
} else {
    # Windows 10/11 (trial installs)
    $features = 'IIS-WebServerRole', 'IIS-WebServer', 'IIS-WebSockets', 'IIS-ApplicationInit', 'IIS-HttpRedirect', 'IIS-ManagementConsole'
    foreach ($feature in $features) {
        if ((Get-WindowsOptionalFeature -Online -FeatureName $feature).State -ne 'Enabled') {
            Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart | Out-Null
        }
    }
}
if (-not (Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll")) {
    throw 'ASP.NET Core Module missing: install the ASP.NET Core 10 Hosting Bundle (https://dotnet.microsoft.com/download/dotnet/10.0), run iisreset, then run this script again.'
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd not found: install the SQL Server command line tools (sqlcmd) and open a new PowerShell.'
}
Import-Module WebAdministration

Write-Host '== 2. Folders'
$paths = @{
    App         = Join-Path $Root 'app'
    Attachments = Join-Path $Root 'data\attachments'
    Logs        = Join-Path $Root 'logs'
    Backups     = Join-Path $Root 'backups'
    Config      = Join-Path $Root 'config'
    Ops         = Join-Path $Root 'ops'
}
foreach ($dir in $paths.Values + (Join-Path $paths.Backups 'db') + (Join-Path $paths.Backups 'files')) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

Write-Host '== 3. App pool'
if (-not (Test-Path "IIS:\AppPools\$SiteName")) { New-WebAppPool -Name $SiteName | Out-Null }
$pool = "IIS:\AppPools\$SiteName"
Set-ItemProperty $pool -Name managedRuntimeVersion -Value ''
Set-ItemProperty $pool -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty $pool -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty $pool -Name processModel.loadUserProfile -Value $true
# One recycle a night (03:30) instead of every 29 hours in office time.
Set-ItemProperty $pool -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Clear-ItemProperty $pool -Name recycling.periodicRestart.schedule
Set-ItemProperty $pool -Name recycling.periodicRestart.schedule -Value @{ value = '03:30:00' }

$settingsFile = Join-Path $paths.Config 'qa-tracker.settings.json'
$appcmd = "$env:windir\System32\inetsrv\appcmd.exe"
& $appcmd set config -section:system.applicationHost/applicationPools "/-[name='$SiteName'].environmentVariables.[name='QATRACKER_SETTINGS']" /commit:apphost 2>$null | Out-Null
& $appcmd set config -section:system.applicationHost/applicationPools "/+[name='$SiteName'].environmentVariables.[name='QATRACKER_SETTINGS',value='$settingsFile']" /commit:apphost | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not set QATRACKER_SETTINGS on the app pool.' }

Write-Host '== 4. Settings file'
function ConvertTo-JsonText([string] $Value) { ($Value | ConvertTo-Json).Trim('"') }
if (Test-Path $settingsFile) {
    Write-Host "  $settingsFile exists - kept as it is."
    $adminPassword = $null
} else {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $signingKey = [Convert]::ToBase64String($bytes)
    # Temporary: the admin must change it at the first sign-in; deploy.ps1 removes it from the file once seeded.
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $pwBytes = New-Object byte[] 14
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($pwBytes)
    $adminPassword = 'Qa-' + (-join ($pwBytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] }))

    $values = @{
        SqlServer       = $SqlServer
        Database        = $Database
        SigningKey      = $signingKey
        AttachmentsPath = $paths.Attachments
        AppBaseUrl      = "https://$HostName"
        MailFrom        = $MailFrom
        AdminEmail      = $AdminEmail
        AdminPassword   = $adminPassword
        LogPath         = Join-Path $paths.Logs 'qa-tracker-.log'
        HostName        = $HostName
    }
    $json = Get-Content (Join-Path $PSScriptRoot 'qa-tracker.settings.template.json') -Raw
    foreach ($key in $values.Keys) { $json = $json.Replace("{{$key}}", (ConvertTo-JsonText $values[$key])) }
    [IO.File]::WriteAllText($settingsFile, $json, (New-Object Text.UTF8Encoding($false)))
}

Write-Host '== 5. Permissions'
# Settings (secrets): Administrators + SYSTEM full, app pool read - nobody else.
& icacls $paths.Config /inheritance:r /grant:r 'BUILTIN\Administrators:(OI)(CI)F' 'NT AUTHORITY\SYSTEM:(OI)(CI)F' "${appPoolIdentity}:(OI)(CI)R" | Out-Null
& icacls $paths.App /grant "${appPoolIdentity}:(OI)(CI)RX" | Out-Null
& icacls (Join-Path $Root 'data') /grant "${appPoolIdentity}:(OI)(CI)M" | Out-Null
& icacls $paths.Logs /grant "${appPoolIdentity}:(OI)(CI)M" | Out-Null
# Backups hold the whole database: Administrators + SYSTEM, and the SQL Server service that writes the .bak files.
$sqlAccount = Get-SqlServiceAccount $SqlServer
& icacls $paths.Backups /inheritance:r /grant:r 'BUILTIN\Administrators:(OI)(CI)F' 'NT AUTHORITY\SYSTEM:(OI)(CI)F' "${sqlAccount}:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not give $sqlAccount write access to $($paths.Backups)." }

Write-Host '== 6. Database'
& (Join-Path $release 'database\apply-scripts.ps1') -Server $SqlServer -Database $Database
# App: read/write data only (no DDL - schema changes come from apply-scripts.ps1 run by an administrator).
# Backup task (SYSTEM): BACKUP DATABASE via db_backupoperator; RESTORE VERIFYONLY needs CREATE ANY DATABASE.
Invoke-Sql $SqlServer 'master' @"
IF SUSER_ID(N'$appPoolIdentity') IS NULL CREATE LOGIN [$appPoolIdentity] FROM WINDOWS;
IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;
GRANT CREATE ANY DATABASE TO [NT AUTHORITY\SYSTEM];
"@ | Out-Null
Invoke-Sql $SqlServer $Database @"
IF USER_ID(N'$appPoolIdentity') IS NULL CREATE USER [$appPoolIdentity] FOR LOGIN [$appPoolIdentity];
ALTER ROLE db_datareader ADD MEMBER [$appPoolIdentity];
ALTER ROLE db_datawriter ADD MEMBER [$appPoolIdentity];
IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];
"@ | Out-Null

Write-Host '== 7. Site and HTTPS'
if (-not $CertificateThumbprint) {
    if (-not $SelfSignedCertificate) { throw 'Pass -CertificateThumbprint (certificate for the host name in LocalMachine\My) or -SelfSignedCertificate.' }
    $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq "CN=$HostName" -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
    if (-not $cert) { $cert = New-SelfSignedCertificate -DnsName $HostName -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(2) }
    $CertificateThumbprint = $cert.Thumbprint
    Write-Warning "Self-signed certificate $CertificateThumbprint - trust it on the team's machines or replace it with a real one."
}
$CertificateThumbprint = $CertificateThumbprint -replace '\s', ''
if (-not (Test-Path "Cert:\LocalMachine\My\$CertificateThumbprint")) { throw "Certificate $CertificateThumbprint not found in Cert:\LocalMachine\My." }

if (-not (Get-Website -Name $SiteName)) {
    New-Website -Name $SiteName -PhysicalPath $paths.App -ApplicationPool $SiteName -Port 443 -Ssl -HostHeader $HostName -SslFlags 1 | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $paths.App
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $SiteName
}
if (-not (Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName)) {
    New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1
}
(Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName).AddSslCertificate($CertificateThumbprint, 'My')
# Port 80 only redirects (the app answers http with a redirect to https, HSTS keeps browsers on https).
if (-not (Get-WebBinding -Name $SiteName -Protocol http -Port 80 -HostHeader $HostName)) {
    New-WebBinding -Name $SiteName -Protocol http -Port 80 -HostHeader $HostName
}
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true
foreach ($port in 80, 443) {
    $rule = "QA Tracker HTTP $port"
    if (-not (Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow | Out-Null
    }
}
# The deploy smoke test calls https://<host>/health from this machine.
try { [Net.Dns]::GetHostAddresses($HostName) | Out-Null } catch {
    Add-Content "$env:windir\System32\drivers\etc\hosts" "`r`n127.0.0.1`t$HostName" -Encoding ASCII
    Write-Warning "$HostName did not resolve - added it to the hosts file of this server. Add a DNS record for the team."
}

Write-Host '== 8. Install record and ops scripts'
$install = [ordered]@{
    Root                = $Root
    SiteName            = $SiteName
    HostName            = $HostName
    SqlServer           = $SqlServer
    Database            = $Database
    SettingsFile        = $settingsFile
    BackupRetentionDays = $BackupRetentionDays
    BackupCopyTo        = $BackupCopyTo
    InstalledAt         = (Get-Date).ToString('o')
}
[IO.File]::WriteAllText((Get-InstallConfigPath $Root), ($install | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
Copy-Item (Join-Path $PSScriptRoot '*.ps1') $paths.Ops -Force

Write-Host '== 9. Nightly backup task'
$taskName = 'QA Tracker nightly backup'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $paths.Ops 'backup.ps1')`" -Root `"$Root`""
$trigger = New-ScheduledTaskTrigger -Daily -At $BackupTime
$taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $taskSettings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null

Write-Host ''
Write-Host "Server ready. Next: .\deploy\deploy.ps1 -Root $Root"
if ($adminPassword) {
    Write-Host ''
    Write-Host "First sign-in: user 'admin', temporary password  $adminPassword"
    Write-Host '(shown once; it must be changed at the first sign-in and is removed from the settings file by deploy.ps1)'
}
