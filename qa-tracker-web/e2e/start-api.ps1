<#
.SYNOPSIS
  Starts the API for the Playwright E2E run on a fresh throw-away database (QaTracker_E2E on .\SQLEXPRESS).
  Called by playwright.config.ts (webServer). The admin password and signing key come from its environment
  (E2E_ADMIN_PASSWORD, E2E_SIGNING_KEY: random per run, never stored).
#>
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$db = 'QaTracker_E2E'

& sqlcmd -S '.\SQLEXPRESS' -E -b -Q "IF DB_ID(N'$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not drop $db" }
& (Join-Path $root 'database\apply-scripts.ps1') -Database $db | Out-Null

$storage = Join-Path ([IO.Path]::GetTempPath()) 'qa-tracker-e2e-attachments'
if (Test-Path $storage) { Remove-Item $storage -Recurse -Force }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__QaTracker = "Server=.\SQLEXPRESS;Database=$db;Trusted_Connection=True;TrustServerCertificate=True"
$env:Auth__SigningKey = $env:E2E_SIGNING_KEY
$env:Seed__AdminPassword = $env:E2E_ADMIN_PASSWORD
$env:Database__SeedOnStartup = 'true'
$env:Email__Enabled = 'false'
$env:Storage__Root = $storage
$env:Cors__AllowedOrigins__0 = 'http://localhost:4300'
$env:Serilog__MinimumLevel__Default = 'Warning'

& dotnet run --project (Join-Path $root 'src\QaTracker.Api') --no-launch-profile --urls 'http://localhost:5129'
