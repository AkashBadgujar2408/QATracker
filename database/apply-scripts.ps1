<#
.SYNOPSIS
  Applies database/scripts/NNN_*.sql to a QA Tracker database, in order, each script once.

.DESCRIPTION
  Creates the database if it does not exist, then runs every script that is not yet recorded in
  meta.SchemaVersions (000_CreateDatabase.sql is skipped - this script creates the database itself).
  The meta schema is ignored by Scaffold-DbContext (-Schemas dbo).

.EXAMPLE
  .\database\apply-scripts.ps1
  .\database\apply-scripts.ps1 -Server ".\SQLEXPRESS" -Database "QaTracker_Dev2"
#>
param(
    [string] $Server = '.\SQLEXPRESS',
    [string] $Database = 'QaTracker'
)

$ErrorActionPreference = 'Stop'
$scriptsDir = Join-Path $PSScriptRoot 'scripts'

function Invoke-Sql([string] $Db, [string] $Query) {
    $output = & sqlcmd -S $Server -E -d $Db -b -I -h -1 -W -Q "SET NOCOUNT ON; $Query"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $output" }
    return $output
}

Write-Host "Server: $Server  Database: $Database"

Invoke-Sql 'master' "IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database];" | Out-Null

Invoke-Sql $Database @"
IF SCHEMA_ID(N'meta') IS NULL EXEC(N'CREATE SCHEMA meta');
IF OBJECT_ID(N'meta.SchemaVersions') IS NULL
    CREATE TABLE meta.SchemaVersions (ScriptName nvarchar(200) NOT NULL PRIMARY KEY, AppliedAt datetimeoffset NOT NULL);
"@ | Out-Null

$applied = @(Invoke-Sql $Database 'SELECT ScriptName FROM meta.SchemaVersions;') | ForEach-Object { $_.Trim() } | Where-Object { $_ }

$scripts = Get-ChildItem $scriptsDir -Filter '*.sql' | Where-Object { $_.Name -match '^\d{3}_' -and $_.Name -ne '000_CreateDatabase.sql' } | Sort-Object Name
foreach ($script in $scripts) {
    if ($applied -contains $script.Name) {
        Write-Host "  skip   $($script.Name)"
        continue
    }
    Write-Host "  apply  $($script.Name)"
    & sqlcmd -S $Server -E -d $Database -b -I -i $script.FullName
    if ($LASTEXITCODE -ne 0) { throw "Script $($script.Name) failed - nothing recorded for it." }
    Invoke-Sql $Database "INSERT meta.SchemaVersions (ScriptName, AppliedAt) VALUES (N'$($script.Name)', SYSDATETIMEOFFSET());" | Out-Null
}

Write-Host 'Database is up to date.'
