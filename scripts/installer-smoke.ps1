<#
.SYNOPSIS
  Install -> smoke -> upgrade -> smoke -> uninstall, checking that user data survives (ADR-008).
.DESCRIPTION
  Installer-neutral harness. Each installer technology gets an adapter (install / uninstall / installed exe path).
  Only the 'Xcopy' adapter exists until the owner picks the installer (ADR-008). It copies a publish folder to a
  per-user location, which proves the harness and binary-level upgrade compatibility, but NOT installer behavior.

  Checks:
    1. The old build installs and passes the smoke on a fresh data folder (it creates one character).
    2. The new build installs over it and passes the smoke, and the character from step 1 is still there.
    3. If the database schema version changed, tomestack.db.v<old>.bak exists (backup before migration).
    4. After uninstall, the data folder and database still exist.

  -DataDir defaults to a throwaway folder. On a clean VM, pass -DataDir "$env:LOCALAPPDATA\TomeStack" to check the
  real default location. Never do that on a machine with real TomeStack data.
.EXAMPLE
  scripts/installer-smoke.ps1 -Adapter Xcopy -OldBuild artifacts/old -NewBuild artifacts/new
#>
param(
  [Parameter(Mandatory)] [ValidateSet('Xcopy')] [string] $Adapter,
  [Parameter(Mandatory)] [string] $OldBuild,
  [Parameter(Mandatory)] [string] $NewBuild,
  [string] $DataDir,
  [string] $InstallRoot
)
$ErrorActionPreference = 'Stop'
$smoke = Join-Path $PSScriptRoot 'smoke.ps1'
if (-not $DataDir) { $DataDir = Join-Path ([IO.Path]::GetTempPath()) "tomestack-installer-smoke-$([guid]::NewGuid().ToString('N'))" }
if (-not $InstallRoot) { $InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\TomeStack-installer-smoke" }

# --- adapters -------------------------------------------------------------------------------------------------
function Install-Build([string] $build) {
  switch ($Adapter) {
    'Xcopy' {
      if (Test-Path $InstallRoot) { Remove-Item $InstallRoot -Recurse -Force }   # replace the program folder only
      New-Item -ItemType Directory -Path $InstallRoot | Out-Null
      Copy-Item -Path (Join-Path $build '*') -Destination $InstallRoot -Recurse
    }
  }
}
function Get-InstalledExe { Join-Path $InstallRoot 'TomeStack.exe' }
function Uninstall-App {
  switch ($Adapter) { 'Xcopy' { Remove-Item $InstallRoot -Recurse -Force } }
}
# --------------------------------------------------------------------------------------------------------------

function Invoke-Step([string] $name, [int] $expectCharacters) {
  $report = Join-Path ([IO.Path]::GetTempPath()) "tomestack-installer-smoke-$([guid]::NewGuid().ToString('N')).json"
  & $smoke -Exe (Get-InstalledExe) -DataDir $DataDir -Report $report -ExpectCharactersAtStart $expectCharacters | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "$name failed" }
  return (Get-Content $report -Raw | ConvertFrom-Json)
}

Write-Output "data dir: $DataDir"
Install-Build $OldBuild
$old = Invoke-Step 'old build smoke' 0
Write-Output "PASS 1: old build v$($old.appVersion) (schema $($old.schemaVersion)) installed and ran"

Install-Build $NewBuild
$new = Invoke-Step 'new build smoke' 1
Write-Output "PASS 2: new build v$($new.appVersion) (schema $($new.schemaVersion)) upgraded in place; data preserved"

$database = Join-Path $DataDir 'tomestack.db'
if ($new.schemaVersion -ne $old.schemaVersion) {
  $backup = "$database.v$($old.schemaVersion).bak"
  if (-not (Test-Path $backup)) { throw "schema changed $($old.schemaVersion) -> $($new.schemaVersion) but $backup is missing" }
  Write-Output "PASS 3: schema $($old.schemaVersion) -> $($new.schemaVersion); backup-before-migration present: $(Split-Path $backup -Leaf)"
} else {
  Write-Output "SKIP 3: schema unchanged ($($new.schemaVersion)); no backup expected"
}

Uninstall-App
if (-not (Test-Path $database)) { throw 'uninstall removed the database' }
Write-Output "PASS 4: uninstalled; data folder preserved ($database)"
