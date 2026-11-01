<#
.SYNOPSIS
  Builds a deployable QA Tracker release zip (ADR-042) on a developer machine.

.DESCRIPTION
  .\deploy\build-release.ps1             tests + build -> artifacts\release\qa-tracker-<yyyyMMdd>-<commit>.zip
  .\deploy\build-release.ps1 -SkipTests  build only (the session gate still runs the full suites)

  The zip holds
    app\        dotnet publish (Release, win-x64, framework-dependent - needs the ASP.NET Core 10 Hosting Bundle on the server)
                with the Angular production build in app\wwwroot; no appsettings.Development.json
    database\   apply-scripts.ps1 + scripts\NNN_*.sql
    deploy\     install-server.ps1, deploy.ps1, backup.ps1, restore.ps1, ops-common.ps1, settings template, README
    version.txt
  Refuses to build from a working tree with uncommitted changes unless -AllowDirty.
#>
param(
    [string] $Output,
    [switch] $SkipTests,
    [switch] $AllowDirty
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $repo 'artifacts\release' }

function Invoke-Step([string] $Title, [scriptblock] $Command) {
    Write-Host "== $Title"
    # Native tools write warnings to stderr; in Windows PowerShell that must not count as a failure - the exit code does.
    $ErrorActionPreference = 'Continue'
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Title failed (exit code $LASTEXITCODE)." }
}

Push-Location $repo
try {
    $dirty = git status --porcelain
    if ($dirty -and -not $AllowDirty) { throw 'Uncommitted changes - commit first (or pass -AllowDirty for a trial build).' }
    $commit = (git rev-parse --short HEAD).Trim()
    $version = '{0:yyyyMMdd}-{1}{2}' -f (Get-Date), $commit, $(if ($dirty) { '-dirty' } else { '' })

    if (-not $SkipTests) {
        Invoke-Step 'Backend tests' { dotnet test }
        Push-Location qa-tracker-web
        try { Invoke-Step 'Frontend tests' { npm test -- --watch=false } } finally { Pop-Location }
    }

    Push-Location qa-tracker-web
    try {
        Invoke-Step 'Frontend install' { npm ci --no-audit --no-fund }
        Invoke-Step 'Frontend build' { npm run build }
    } finally { Pop-Location }

    $stage = Join-Path $Output "qa-tracker-$version"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    Invoke-Step 'API publish' { dotnet publish src/QaTracker.Api -c Release -r win-x64 --self-contained false -o (Join-Path $stage 'app') --nologo }
    $app = Join-Path $stage 'app'
    Remove-Item (Join-Path $app 'appsettings.Development.json') -ErrorAction SilentlyContinue
    foreach ($leftover in 'App_Data', 'logs') {
        if (Test-Path (Join-Path $app $leftover)) { Remove-Item (Join-Path $app $leftover) -Recurse -Force }
    }
    Copy-Item (Join-Path $repo 'qa-tracker-web\dist\qa-tracker-web\browser') (Join-Path $app 'wwwroot') -Recurse
    if (-not (Test-Path (Join-Path $app 'wwwroot\index.html'))) { throw 'Angular build missing (wwwroot\index.html).' }

    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'database') | Out-Null
    Copy-Item (Join-Path $repo 'database\apply-scripts.ps1') (Join-Path $stage 'database')
    Copy-Item (Join-Path $repo 'database\scripts') (Join-Path $stage 'database\scripts') -Recurse
    Copy-Item $PSScriptRoot (Join-Path $stage 'deploy') -Recurse
    Remove-Item (Join-Path $stage 'deploy\build-release.ps1')
    Set-Content (Join-Path $stage 'version.txt') $version -Encoding ASCII

    $zip = Join-Path $Output "qa-tracker-$version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host ''
    Write-Host "Release $version -> $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
} finally {
    Pop-Location
}
