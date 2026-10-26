<#
.SYNOPSIS
  Runs TomeStack.exe --smoke and checks its JSON report.
.DESCRIPTION
  The smoke loads the bundled UI over the WebView2 virtual host, round-trips app.info and character.list over the
  message bridge, then creates a fixture character, exports it and previews the package (M0 exit gate). Since M2
  item 6 it also attaches a generated PDF to a throwaway homebrew source and opens page 2 in the offline PDF viewer
  window (ADR-005). It fails if the UI or the viewer attempted any network request. A plain passing run then starts a
  second process with --simulate-unhandled-exception on a fresh data folder and checks that it exits non-zero and wrote
  a dispatcher entry to errors.log (crashCheck in the report). -SkipCrashCheck omits that step; use it for any build
  that predates the switch (installer-smoke's old build) and for the other check scripts. Exit code: 0 pass, 1 fail.
.EXAMPLE
  scripts/smoke.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe
.EXAMPLE
  # Persistence across restarts: the second run must see the character created by the first.
  scripts/smoke.ps1 -Exe $exe -DataDir $dir -ExpectCharactersAtStart 0
  scripts/smoke.ps1 -Exe $exe -DataDir $dir -ExpectCharactersAtStart 1
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [string] $DataDir,
  [string] $Report = (Join-Path ([IO.Path]::GetTempPath()) "tomestack-smoke-$([guid]::NewGuid().ToString('N')).json"),
  [int] $ExpectCharactersAtStart = -1,
  [string] $ExpectDetail = 'ok',
  [int] $TimeoutSeconds = 60,
  [hashtable] $Environment = @{},
  [string[]] $ExtraArguments = @(),
  [switch] $SkipCrashCheck
)
$ErrorActionPreference = 'Stop'

$arguments = @('--smoke', '--smoke-report', $Report) + $ExtraArguments
if ($DataDir) { $arguments += @('--data-dir', $DataDir) }
Remove-Item $Report -ErrorAction SilentlyContinue

$saved = @{}
foreach ($key in $Environment.Keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key); [Environment]::SetEnvironmentVariable($key, $Environment[$key]) }
try {
  $process = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    Write-Output "FAIL: no exit within $TimeoutSeconds s"
    exit 1
  }
} finally {
  foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
}

if (-not (Test-Path $Report)) { Write-Output "FAIL: exit $($process.ExitCode), no report at $Report"; exit 1 }
$result = Get-Content $Report -Raw | ConvertFrom-Json
Write-Output (Get-Content $Report -Raw)

# Without -DataDir the app used a throwaway folder under %TEMP%\tomestack-smoke; remove it. Only that folder is touched.
$throwawayRoot = Join-Path ([IO.Path]::GetTempPath()) 'tomestack-smoke'
if (-not $DataDir -and $result.dataDirectory -and ([IO.Path]::GetFullPath($result.dataDirectory)).StartsWith($throwawayRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  Remove-Item -LiteralPath $result.dataDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

$problems = @()
if ($result.detail -ne $ExpectDetail) { $problems += "detail '$($result.detail)' (expected '$ExpectDetail')" }
if ($ExpectDetail -eq 'ok' -and $process.ExitCode -ne 0) { $problems += "exit code $($process.ExitCode)" }
if (@($result.blockedRequests).Count -gt 0) { $problems += "blocked network requests: $($result.blockedRequests -join ', ')" }
if ($ExpectCharactersAtStart -ge 0 -and $result.charactersAtStart -ne $ExpectCharactersAtStart) {
  $problems += "charactersAtStart $($result.charactersAtStart) (expected $ExpectCharactersAtStart)"
}
if ($problems.Count -gt 0) { Write-Output "FAIL: $($problems -join '; ')"; exit 1 }

# Crash handler: a forced unhandled exception on the UI thread must exit non-zero and leave a dispatcher entry in
# errors.log. Only on the plain passing run, so offline-check.ps1's runs (extra arguments, other expected details) are unchanged.
if (-not $SkipCrashCheck -and $ExpectDetail -eq 'ok' -and $ExtraArguments.Count -eq 0) {
  $smokeRoot = Join-Path ([IO.Path]::GetTempPath()) 'tomestack-smoke'
  New-Item -ItemType Directory -Path $smokeRoot -Force | Out-Null
  $crashDir = Join-Path $smokeRoot "crash-$([guid]::NewGuid().ToString('N'))"
  $crashReport = Join-Path $smokeRoot "crash-$([guid]::NewGuid().ToString('N')).json"
  $crashArguments = @('--smoke', '--simulate-unhandled-exception', '--data-dir', $crashDir, '--smoke-report', $crashReport)
  $crashFailure = $null
  try {
    $crash = Start-Process -FilePath $Exe -ArgumentList $crashArguments -PassThru
    if (-not $crash.WaitForExit($TimeoutSeconds * 1000)) {
      $crash.Kill()
      $crashFailure = "FAIL: crash handler: no exit within $TimeoutSeconds s"
    } else {
      $crashLog = Join-Path $crashDir 'logs\errors.log'
      $crashText = if (Test-Path $crashLog) { Get-Content $crashLog -Raw } else { '' }
      $logged = $crashText -match 'command=dispatcher' -and $crashText -match 'Simulated by --simulate-unhandled-exception'
      $crashCheck = @{ exitCode = $crash.ExitCode; logged = [bool]$logged }
      $result | Add-Member -NotePropertyName crashCheck -NotePropertyValue $crashCheck -Force
      $result | ConvertTo-Json -Depth 10 | Set-Content -Path $Report -Encoding UTF8
      Write-Output "crashCheck: $($crashCheck | ConvertTo-Json -Compress)"
      if ($crash.ExitCode -eq 0) { $crashFailure = 'FAIL: crash handler: exit code 0 after a forced unhandled exception' }
      elseif (-not $logged) { $crashFailure = 'FAIL: crash handler: errors.log lacks command=dispatcher and the simulated exception' }
    }
  } finally {
    $full = [IO.Path]::GetFullPath($crashDir)
    if ($full.StartsWith($smokeRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
      Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $crashReport -ErrorAction SilentlyContinue
  }
  if ($crashFailure) { Write-Output $crashFailure; exit 1 }
}
Write-Output 'PASS'
exit 0
