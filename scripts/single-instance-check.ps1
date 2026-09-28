<#
.SYNOPSIS
  Two TomeStack.exe processes on one data folder: the second must hand over to the first and leave the data alone (M2.1).
.DESCRIPTION
  1. Starts TomeStack.exe normally (no --smoke) on a throwaway data folder and waits for its window.
  2. Starts a second TomeStack.exe --smoke on the same folder. It must exit with code 3 and report
     detail 'data-folder-in-use' with activatedExisting = true (it signalled the first window to come forward).
  3. Checks the first process is still running, then closes it.
  4. Runs the normal smoke on the same folder: the lock was released, and the folder opens and works.
  Exit code: 0 pass, 1 fail. The throwaway folder is deleted afterwards.
.EXAMPLE
  scripts/single-instance-check.ps1 -Exe src/DesktopShell/bin/Release/net10.0-windows/TomeStack.exe
#>
param(
  [Parameter(Mandatory)] [string] $Exe,
  [int] $TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'

$dataDir = Join-Path ([IO.Path]::GetTempPath()) "tomestack-single-instance-$([guid]::NewGuid().ToString('N'))"
$report = Join-Path ([IO.Path]::GetTempPath()) "tomestack-second-$([guid]::NewGuid().ToString('N')).json"
$problems = @()
$first = $null
try {
  $first = Start-Process -FilePath $Exe -ArgumentList @('--data-dir', $dataDir) -PassThru
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  # The window is shown only after the data folder is open (and locked).
  while ((Get-Date) -lt $deadline -and -not $first.HasExited) {
    $first.Refresh()
    if ($first.MainWindowHandle -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 200
  }
  if ($first.HasExited) { throw "first instance exited early (code $($first.ExitCode))" }
  if ($first.MainWindowHandle -eq [IntPtr]::Zero) { throw "first instance showed no window within $TimeoutSeconds s" }

  $second = Start-Process -FilePath $Exe -ArgumentList @('--smoke', '--smoke-report', $report, '--data-dir', $dataDir) -PassThru
  if (-not $second.WaitForExit($TimeoutSeconds * 1000)) { $second.Kill(); throw "second instance did not exit within $TimeoutSeconds s" }
  if ($second.ExitCode -ne 3) { $problems += "second instance exit code $($second.ExitCode) (expected 3)" }
  if (-not (Test-Path $report)) { $problems += 'second instance wrote no report' }
  else {
    $result = Get-Content $report -Raw | ConvertFrom-Json
    Write-Output (Get-Content $report -Raw)
    if ($result.detail -ne 'data-folder-in-use') { $problems += "second instance detail '$($result.detail)'" }
    if (-not $result.activatedExisting) { $problems += 'second instance did not signal the first' }
  }
  if ($first.HasExited) { $problems += "first instance exited (code $($first.ExitCode)) when the second started" }

  [void]$first.CloseMainWindow()
  if (-not $first.WaitForExit($TimeoutSeconds * 1000)) { $first.Kill(); $problems += 'first instance did not close' }

  # The folder is free again and still works: the full smoke on it passes.
  $smoke = & (Join-Path $PSScriptRoot 'smoke.ps1') -Exe $Exe -DataDir $dataDir -ExpectCharactersAtStart 0
  Write-Output $smoke
  if ($LASTEXITCODE -ne 0) { $problems += 'smoke on the released folder failed' }
} catch {
  $problems += $_.Exception.Message
} finally {
  if ($first -and -not $first.HasExited) { $first.Kill() }
  Remove-Item $report -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  Get-ChildItem -LiteralPath $dataDir -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $_.IsReadOnly = $false }
  Remove-Item -LiteralPath $dataDir -Recurse -Force -ErrorAction SilentlyContinue
}

if ($problems.Count -gt 0) { Write-Output "FAIL: $($problems -join '; ')"; exit 1 }
Write-Output 'PASS'
exit 0
