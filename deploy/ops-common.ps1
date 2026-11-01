# Shared helpers for the QA Tracker ops scripts (dot-sourced; Windows PowerShell 5.1 compatible).

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from an elevated PowerShell (Run as administrator).'
    }
}

function Get-InstallConfigPath([string] $Root) { Join-Path $Root 'config\install.json' }

# install.json is written by install-server.ps1 so deploy / backup / restore need no parameters.
function Read-InstallConfig([string] $Root) {
    $path = Get-InstallConfigPath $Root
    if (-not (Test-Path $path)) { throw "No $path - run deploy\install-server.ps1 first." }
    return Get-Content $path -Raw | ConvertFrom-Json
}

function Write-OpsLog([string] $Root, [string] $Name, [string] $Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Message
    Write-Host $line
    $dir = Join-Path $Root 'logs'
    if (Test-Path $dir) { Add-Content -Path (Join-Path $dir "$Name.log") -Value $line -Encoding UTF8 }
}

function Invoke-Sql([string] $Server, [string] $Database, [string] $Query) {
    $output = & sqlcmd -S $Server -E -d $Database -b -I -h -1 -W -Q "SET NOCOUNT ON; $Query" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $output" }
    return $output
}

# Windows service account of a SQL Server instance ('.\SQLEXPRESS' -> NT SERVICE\MSSQL$SQLEXPRESS); it writes the .bak files.
function Get-SqlServiceAccount([string] $Server) {
    $instance = if ($Server -match '\\(.+)$') { $Matches[1] } else { 'MSSQLSERVER' }
    $serviceName = if ($instance -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { "MSSQL`$$instance" }
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if ($service -and $service.StartName -and $service.StartName -ne 'LocalSystem') { return $service.StartName }
    return "NT SERVICE\$serviceName"
}

function Test-AppHealth([string] $HostName, [int] $TimeoutSeconds = 90) {
    # The server certificate may be self-signed or issued for the host name only: this check trusts it.
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    $url = "https://$HostName/health"
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15
            if ($response.StatusCode -eq 200) { return $true }
        } catch {
            Write-Host "  waiting for $url ... ($($_.Exception.Message))"
        }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Remove-OldFiles([string] $Folder, [int] $Days) {
    if (-not (Test-Path $Folder)) { return @() }
    $limit = (Get-Date).AddDays(-$Days)
    $old = Get-ChildItem $Folder -File | Where-Object { $_.LastWriteTime -lt $limit }
    $old | Remove-Item -Force
    return $old
}
