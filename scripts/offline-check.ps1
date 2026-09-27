<#
.SYNOPSIS
  Offline evidence for ADR-001 / ADR-006 / ADR-008.
.DESCRIPTION
  -Mode MissingRuntime        (automated, CI) Runs the smoke with the test-only --simulate-missing-webview2 flag, which
                              takes the shell's "runtime not found" path. Expects 'webview2-runtime-missing' (the
                              in-window "runtime required" message, exit 2) instead of a crash. Deterministic on any
                              machine, because it does not depend on WebView2 loader overrides.
  -Mode MissingRuntimeLoader  (local diagnostic) Points the real WebView2 loader at an empty folder with the documented
                              WEBVIEW2_BROWSER_EXECUTABLE_FOLDER override. Passes locally under Windows PowerShell 5.1
                              and pwsh 7, but a hosted GitHub runner ignored the override (ADR-006), so CI does not use it.
  -Mode AssumeOffline         (manual) Turn on airplane mode or unplug the network first. The script checks that there
                              is no default route and no DNS, then runs the smoke twice on one data folder (persistence).
  Neither missing-runtime mode removes the runtime; a truly absent runtime still needs a clean VM.
  Exit code: 0 pass, 1 fail.
#>
param(
  [Parameter(Mandatory)] [ValidateSet('MissingRuntime', 'MissingRuntimeLoader', 'AssumeOffline')] [string] $Mode,
  [string] $Exe
)
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so resolve here.
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot '..\src\DesktopShell\bin\Release\net10.0-windows\TomeStack.exe' }
$smoke = Join-Path $PSScriptRoot 'smoke.ps1'

if ($Mode -eq 'MissingRuntime') {
  & $smoke -Exe $Exe -ExpectDetail 'webview2-runtime-missing' -ExtraArguments @('--simulate-missing-webview2')
  exit $LASTEXITCODE
}

if ($Mode -eq 'MissingRuntimeLoader') {
  $empty = Join-Path ([IO.Path]::GetTempPath()) "tomestack-no-webview2-$([guid]::NewGuid().ToString('N'))"
  New-Item -ItemType Directory -Path $empty | Out-Null
  & $smoke -Exe $Exe -ExpectDetail 'webview2-runtime-missing' -Environment @{ WEBVIEW2_BROWSER_EXECUTABLE_FOLDER = $empty }
  $code = $LASTEXITCODE
  Remove-Item -LiteralPath $empty -Force -ErrorAction SilentlyContinue
  exit $code
}

$routes = @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue)
$dnsWorks = $true
try { [void][Net.Dns]::GetHostAddresses('www.microsoft.com') } catch { $dnsWorks = $false }
if ($routes.Count -gt 0 -or $dnsWorks) {
  Write-Output 'REFUSED: this machine is still online (default route or DNS). Turn on airplane mode first.'
  exit 1
}

$dir = Join-Path ([IO.Path]::GetTempPath()) "tomestack-offline-$([guid]::NewGuid().ToString('N'))"
& $smoke -Exe $Exe -DataDir $dir -ExpectCharactersAtStart 0
if ($LASTEXITCODE -ne 0) { exit 1 }
& $smoke -Exe $Exe -DataDir $dir -ExpectCharactersAtStart 1
if ($LASTEXITCODE -ne 0) { exit 1 }
Write-Output 'PASS (offline)'
exit 0
