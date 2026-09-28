<#
.SYNOPSIS
  Offline evidence for ADR-001 / ADR-006 / ADR-008.
.DESCRIPTION
  -Mode MissingRuntime  (automated) Points the WebView2 loader at an empty folder via the documented
                        WEBVIEW2_BROWSER_EXECUTABLE_FOLDER override. Expects the smoke to report
                        'webview2-runtime-missing' (the shell shows its "runtime required" message) instead of crashing.
                        This simulates a missing runtime; a clean machine without the runtime still needs a VM.
  -Mode AssumeOffline   (manual) Turn on airplane mode or unplug the network first. The script checks that there is
                        no default route and no DNS, then runs the smoke twice on one data folder (persistence).
  Exit code: 0 pass, 1 fail.
#>
param(
  [Parameter(Mandatory)] [ValidateSet('MissingRuntime', 'AssumeOffline')] [string] $Mode,
  [string] $Exe
)
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults, so resolve here.
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot '..\src\DesktopShell\bin\Release\net10.0-windows\TomeStack.exe' }
$smoke = Join-Path $PSScriptRoot 'smoke.ps1'

if ($Mode -eq 'MissingRuntime') {
  $empty = Join-Path ([IO.Path]::GetTempPath()) "tomestack-no-webview2-$([guid]::NewGuid().ToString('N'))"
  New-Item -ItemType Directory -Path $empty | Out-Null
  & $smoke -Exe $Exe -ExpectDetail 'webview2-runtime-missing' -Environment @{ WEBVIEW2_BROWSER_EXECUTABLE_FOLDER = $empty }
  exit $LASTEXITCODE
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
