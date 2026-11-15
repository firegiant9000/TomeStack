<#
.SYNOPSIS
  Checks the smoke ready-time budget in a smoke report (D102). The ready time is an upper bound on first paint, not first paint.
.DESCRIPTION
  Reads processStartToReadyMs (process start to UI ready, written by the --smoke report in MainWindow.xaml.cs) from the
  report. The budget is -MaxReadyMs when positive, otherwise 30000 ms when CI is 'true' (GitHub sets it) and 15000 ms
  locally. A negative -MaxReadyMs skips the check. A report without the field (older builds) is not checked, unless -Require is
  given (smoke.ps1 passes it on a plain run): then a missing or null field fails.
  Exit code: 0 within budget, not reported or not checked; 1 over budget or required and missing.
.EXAMPLE
  scripts/ready-budget.ps1 -Report $report
.EXAMPLE
  scripts/ready-budget.ps1 -Report $report -MaxReadyMs 1
#>
param(
  [Parameter(Mandatory)] [string] $Report,
  [int] $MaxReadyMs = 0,
  [switch] $Require
)
$ErrorActionPreference = 'Stop'

if ($MaxReadyMs -lt 0) { Write-Output 'Smoke ready-time budget not checked.'; exit 0 }
$budget = if ($MaxReadyMs -gt 0) { $MaxReadyMs } elseif ($env:CI -eq 'true') { 30000 } else { 15000 }

$result = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
if ($null -eq $result.processStartToReadyMs) {
  if ($Require) { Write-Output 'Smoke ready time not reported; budget required.'; exit 1 }
  Write-Output 'Smoke ready time not reported; budget not checked.'; exit 0
}

$ready = [long] $result.processStartToReadyMs
if ($ready -gt $budget) { Write-Output "Smoke ready time $ready ms is over the $budget ms budget."; exit 1 }
Write-Output "Smoke ready time $ready ms (budget $budget ms)."
exit 0
