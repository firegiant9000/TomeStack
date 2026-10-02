<#
.SYNOPSIS
  Install -> smoke -> upgrade -> smoke -> uninstall, checking that user data survives (ADR-008).
.DESCRIPTION
  Installer-neutral harness. Each installer technology gets an adapter (install / uninstall / installed exe path).

  Adapters:
    Velopack  -OldBuild/-NewBuild are release folders from scripts/pack-installer.ps1 (they contain
              TomeStack.App-win-Setup.exe). Installs per-user with `Setup.exe --silent` to
              %LOCALAPPDATA%\TomeStack.App, upgrades by running the newer Setup.exe over it, and uninstalls with
              `Update.exe --uninstall --silent`. Refuses to run if TomeStack.App is already installed.
    Xcopy     -OldBuild/-NewBuild are publish folders. Copies them to a per-user folder. Proves the harness and
              binary-level upgrade compatibility, NOT installer behavior.

  Checks:
    1. The old build installs and passes the smoke on a fresh data folder (it creates one character).
    2. The new build installs over it and passes the smoke, and the character from step 1 is still there.
    3. If the database schema version changed, tomestack.db.v<old>.bak exists (backup before migration).
    4. After uninstall, the data folder and database still exist, and so does %LOCALAPPDATA%\TomeStack if it existed
       before (it is only checked, never written).
    5. (Velopack) The installed folder carries LICENSE, NOTICE and ATTRIBUTION.md (ADR-008 R6), and the app version
       increased.

  -InstallOnly (T5, the release workflow, which has no older build): installs -NewBuild alone, smokes it on a fresh data
  folder, checks the notices (step 5 without the version comparison) and uninstalls (step 4). Steps 2 and 3 need an
  older build and are skipped.

  -DataDir defaults to a throwaway folder. On a clean VM, pass -DataDir "$env:LOCALAPPDATA\TomeStack" to check the
  real default location. Never do that on a machine with real TomeStack data.
.EXAMPLE
  scripts/installer-smoke.ps1 -Adapter Velopack -OldBuild artifacts/installer/0.1.0 -NewBuild artifacts/installer/0.1.1
.EXAMPLE
  scripts/installer-smoke.ps1 -Adapter Velopack -InstallOnly -NewBuild artifacts/installer/0.4.0
.EXAMPLE
  scripts/installer-smoke.ps1 -Adapter Xcopy -OldBuild artifacts/old -NewBuild artifacts/new
#>
param(
  [Parameter(Mandatory)] [ValidateSet('Velopack', 'Xcopy')] [string] $Adapter,
  [string] $OldBuild,
  [Parameter(Mandatory)] [string] $NewBuild,
  [string] $DataDir,
  [string] $InstallRoot,
  [switch] $InstallOnly
)
$ErrorActionPreference = 'Stop'
if (-not $InstallOnly -and -not $OldBuild) { throw '-OldBuild is required unless -InstallOnly is given' }
$smoke = Join-Path $PSScriptRoot 'smoke.ps1'
if (-not $DataDir) { $DataDir = Join-Path ([IO.Path]::GetTempPath()) "tomestack-installer-smoke-$([guid]::NewGuid().ToString('N'))" }
$defaultData = Join-Path $env:LOCALAPPDATA 'TomeStack'
$defaultDataExisted = Test-Path $defaultData
if (-not $InstallRoot) {
  $InstallRoot = switch ($Adapter) {
    'Velopack' { Join-Path $env:LOCALAPPDATA 'TomeStack.App' }   # Velopack's fixed per-user location for the pack id
    'Xcopy' { Join-Path $env:LOCALAPPDATA 'Programs\TomeStack-installer-smoke' }
  }
}
if ([IO.Path]::GetFullPath($InstallRoot).TrimEnd('\') -ieq [IO.Path]::GetFullPath($defaultData).TrimEnd('\')) {
  throw "The install folder must not be the data folder ($defaultData); uninstall would delete user data (ADR-008)"
}

# --- adapters -------------------------------------------------------------------------------------------------
function Invoke-Checked([string] $file, [string[]] $arguments) {
  $process = Start-Process -FilePath $file -ArgumentList $arguments -Wait -PassThru
  if ($process.ExitCode -ne 0) { throw "$(Split-Path $file -Leaf) $($arguments -join ' ') exited $($process.ExitCode)" }
}
function Install-Build([string] $build) {
  switch ($Adapter) {
    'Velopack' { Invoke-Checked (Join-Path $build 'TomeStack.App-win-Setup.exe') @('--silent') }
    'Xcopy' {
      if (Test-Path $InstallRoot) { Remove-Item $InstallRoot -Recurse -Force }   # replace the program folder only
      New-Item -ItemType Directory -Path $InstallRoot | Out-Null
      Copy-Item -Path (Join-Path $build '*') -Destination $InstallRoot -Recurse
    }
  }
}
function Get-InstalledExe {
  switch ($Adapter) {
    'Velopack' { Join-Path $InstallRoot 'current\TomeStack.exe' }
    'Xcopy' { Join-Path $InstallRoot 'TomeStack.exe' }
  }
}
function Uninstall-App {
  switch ($Adapter) {
    'Velopack' { Invoke-Checked (Join-Path $InstallRoot 'Update.exe') @('--uninstall', '--silent') }
    'Xcopy' { Remove-Item $InstallRoot -Recurse -Force }
  }
}
# --------------------------------------------------------------------------------------------------------------

function Invoke-Step([string] $name, [int] $expectCharacters) {
  $report = Join-Path ([IO.Path]::GetTempPath()) "tomestack-installer-smoke-$([guid]::NewGuid().ToString('N')).json"
  & $smoke -Exe (Get-InstalledExe) -DataDir $DataDir -Report $report -ExpectCharactersAtStart $expectCharacters | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "$name failed" }
  return (Get-Content $report -Raw | ConvertFrom-Json)
}

if ($Adapter -eq 'Velopack' -and (Test-Path $InstallRoot)) {
  throw "$InstallRoot already exists; uninstall TomeStack first. The harness will not install over an existing copy."
}

Write-Output "adapter: $Adapter; install folder: $InstallRoot; data dir: $DataDir"

if ($InstallOnly) {
  $database = Join-Path $DataDir 'tomestack.db'
  Install-Build $NewBuild
  $new = Invoke-Step 'install smoke' 0
  Write-Output "PASS 1: v$($new.appVersion) (schema $($new.schemaVersion)) installed and ran"
  Write-Output 'SKIP 2, 3: install only; no older build to upgrade from'
  if ($Adapter -eq 'Velopack') {
    $missing = @('LICENSE', 'NOTICE', 'ATTRIBUTION.md', 'THIRD-PARTY-NOTICES-Velopack.md') | Where-Object { -not (Test-Path (Join-Path $InstallRoot "current\$_")) }
    if ($missing) { throw "installed folder lacks $($missing -join ', ')" }
    Write-Output 'PASS 5: LICENSE, NOTICE, ATTRIBUTION.md and the Velopack notices installed'
  }
  Uninstall-App
  if (-not (Test-Path $database)) { throw 'uninstall removed the database' }
  if ($defaultDataExisted -and -not (Test-Path $defaultData)) { throw "uninstall removed $defaultData" }
  if ($Adapter -eq 'Velopack' -and (Test-Path (Get-InstalledExe))) { throw 'uninstall left the app in place' }
  Write-Output "PASS 4: uninstalled; data folder preserved ($database)"
  return
}

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

if ($Adapter -eq 'Velopack') {
  $missing = @('LICENSE', 'NOTICE', 'ATTRIBUTION.md', 'THIRD-PARTY-NOTICES-Velopack.md') | Where-Object { -not (Test-Path (Join-Path $InstallRoot "current\$_")) }
  if ($missing) { throw "installed folder lacks $($missing -join ', ')" }
  if ([version]$new.appVersion -le [version]$old.appVersion) { throw "version did not increase ($($old.appVersion) -> $($new.appVersion))" }
  Write-Output "PASS 5: v$($old.appVersion) -> v$($new.appVersion); LICENSE, NOTICE, ATTRIBUTION.md and the Velopack notices installed"
}

Uninstall-App
if (-not (Test-Path $database)) { throw 'uninstall removed the database' }
if ($defaultDataExisted -and -not (Test-Path $defaultData)) { throw "uninstall removed $defaultData" }
if ($Adapter -eq 'Velopack' -and (Test-Path (Get-InstalledExe))) { throw 'uninstall left the app in place' }
Write-Output "PASS 4: uninstalled; data folder preserved ($database)$(if ($defaultDataExisted) { "; $defaultData untouched" })"
