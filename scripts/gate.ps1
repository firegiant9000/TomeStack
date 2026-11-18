<#
.SYNOPSIS
Runs the TomeStack gate steps 1-7 (CONTRIBUTING.md, "The gate") in order, with logs, a summary and an e2e watchdog.

.DESCRIPTION
Optional helper: the commands in CONTRIBUTING.md stay authoritative. Runs on Windows PowerShell 5.1.
Step ids, in order: npm-ci, lint, ui-test, ui-build, dotnet-build, dotnet-test, e2e.
The run stops at the first failing step; the summary is still printed and written to <LogDir>\summary.txt.
Exit codes: 0 all passed, the failing step's exit code, 124 watchdog timeout (e2e), 2 bad arguments or a CPU check failure under -RequireQuiet.

.PARAMETER From
Start at this step id and run the later steps.
.PARAMETER Only
Run only these step ids (comma separated).
.PARAMETER LogDir
Where the per-step logs and summary.txt go. Default: a time-stamped folder under the temp directory.
.PARAMETER E2eTimeoutMinutes
Watchdog for the e2e step only; the whole process tree is killed on expiry.
.PARAMETER UiWorkers
Sets TOMESTACK_TEST_WORKERS for the children when greater than 0.
.PARAMETER XunitThreads
Adds "-- xUnit.MaxParallelThreads=<n>" to dotnet test when greater than 0.
.PARAMETER ProjectParallelism
Adds "-m:<n>" to dotnet test when greater than 0.
.PARAMETER LowPriority
Starts each step at BelowNormal priority.
.PARAMETER CpuWarnPercent
Warn (or fail under -RequireQuiet) when the average CPU load before the run is at or above this percentage.
.PARAMETER StepsJson
Tests only: a JSON array of {id, exe, args} that replaces the built-in step table.
#>
[CmdletBinding()]
param(
    [string]$From,
    [string[]]$Only,
    [string]$LogDir = (Join-Path (Join-Path $env:TEMP 'tomestack-gate') (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [double]$E2eTimeoutMinutes = 15,
    [int]$UiWorkers,
    [int]$XunitThreads,
    [int]$ProjectParallelism,
    [switch]$LowPriority,
    [int]$CpuWarnPercent = 25,
    [switch]$RequireQuiet,
    [switch]$SkipCpuCheck,
    [switch]$DryRun,
    [string]$StepsJson
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# --- Step table ---
if ($StepsJson) {
    # The parentheses make Windows PowerShell 5.1 enumerate the parsed array instead of passing it on as one object.
    $steps = @((ConvertFrom-Json -InputObject $StepsJson) | ForEach-Object {
            [pscustomobject]@{ id = [string]$_.id; exe = [string]$_.exe; args = @($_.args | ForEach-Object { [string]$_ }) }
        })
}
else {
    $testArgs = @('test', 'TomeStack.slnx', '-c', 'Release')
    if ($ProjectParallelism -gt 0) { $testArgs += "-m:$ProjectParallelism" }
    if ($XunitThreads -gt 0) { $testArgs += @('--', "xUnit.MaxParallelThreads=$XunitThreads") }
    $steps = @(
        [pscustomobject]@{ id = 'npm-ci'; exe = 'npm.cmd'; args = @('ci', '--prefix', 'src/Ui') },
        [pscustomobject]@{ id = 'lint'; exe = 'npm.cmd'; args = @('run', 'lint', '--prefix', 'src/Ui') },
        [pscustomobject]@{ id = 'ui-test'; exe = 'npm.cmd'; args = @('test', '--prefix', 'src/Ui') },
        [pscustomobject]@{ id = 'ui-build'; exe = 'npm.cmd'; args = @('run', 'build', '--prefix', 'src/Ui') },
        [pscustomobject]@{ id = 'dotnet-build'; exe = 'dotnet'; args = @('build', 'TomeStack.slnx', '-c', 'Release') },
        [pscustomobject]@{ id = 'dotnet-test'; exe = 'dotnet'; args = $testArgs },
        [pscustomobject]@{ id = 'e2e'; exe = 'npm.cmd'; args = @('run', 'test:e2e', '--prefix', 'src/Ui') }
    )
}
$validIds = @($steps | ForEach-Object { $_.id })

# --- Select steps ---
$onlyIds = @()
foreach ($o in @($Only)) { if ($o) { $onlyIds += ($o -split ',') | Where-Object { $_ } } }
$bad = @()
if ($From -and ($validIds -notcontains $From)) { $bad += $From }
foreach ($o in $onlyIds) { if ($validIds -notcontains $o) { $bad += $o } }
if ($bad.Count -gt 0) {
    Write-Host ("Unknown step id: {0}. Valid ids: {1}" -f ($bad -join ', '), ($validIds -join ', '))
    exit 2
}
$selected = @($steps)
if ($From) {
    $index = [array]::IndexOf($validIds, $From)
    $selected = @($steps | Select-Object -Skip $index)
}
if ($onlyIds.Count -gt 0) { $selected = @($selected | Where-Object { $onlyIds -contains $_.id }) }

function Format-Arg([string]$a) {
    if ($a -match '[\s"]') { return '"' + ($a -replace '"', '\"') + '"' }
    return $a
}

if ($DryRun) {
    # The CPU check below still runs under -DryRun so that -RequireQuiet can be exercised.
    foreach ($s in $selected) { Write-Host ("{0}: {1} {2}" -f $s.id, $s.exe, (($s.args | ForEach-Object { Format-Arg $_ }) -join ' ')) }
}

# --- CPU check ---
if (-not $SkipCpuCheck) {
    try {
        $counter = '\Processor(_Total)\% Processor Time'
        $samples = Get-Counter -Counter $counter -SampleInterval 1 -MaxSamples 3
        $average = ($samples | ForEach-Object { $_.CounterSamples[0].CookedValue } | Measure-Object -Average).Average
        $line = 'Average {0} was {1:N0}% over 3 s (threshold {2}%).' -f $counter, $average, $CpuWarnPercent
        if ($average -ge $CpuWarnPercent) {
            if ($RequireQuiet) { Write-Host "Machine is not quiet. $line"; exit 2 }
            Write-Warning "Machine is busy; timings and flakes may not be representative. $line"
        }
        else { Write-Host $line }
    }
    catch {
        if ($RequireQuiet) { Write-Host 'CPU check unavailable and -RequireQuiet was given.'; exit 2 }
        Write-Warning 'CPU check unavailable.'
    }
}

if ($DryRun) { exit 0 }

# --- Helpers ---
function Stop-DevHost {
    # The e2e harness runs `dotnet <repo>\src\DevHost\bin\...\TomeStack.DevHost.dll`; an apphost TomeStack.DevHost.exe is covered too.
    # Only processes belonging to this repo root are touched.
    $count = 0
    try {
        $devHostDir = Join-Path $repoRoot 'src\DevHost'
        $found = Get-CimInstance Win32_Process | Where-Object {
            ($_.Name -like 'TomeStack.DevHost*.exe' -and $_.ExecutablePath -and $_.ExecutablePath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) -or
            ($_.Name -ieq 'dotnet.exe' -and $_.CommandLine -and $_.CommandLine.IndexOf($devHostDir, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $_.CommandLine -like '*TomeStack.DevHost*')
        }
        foreach ($p in $found) {
            Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
            $count++
        }
    }
    catch { Write-Warning 'DevHost cleanup unavailable.' }
    Write-Host "DevHost cleanup: stopped $count process(es)."
}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
if ($UiWorkers -gt 0) { $env:TOMESTACK_TEST_WORKERS = [string]$UiWorkers }

$results = @()
$exitCode = 0
Stop-DevHost
try {
    foreach ($s in $selected) {
        Write-Host ("[{0}] {1} {2}" -f $s.id, $s.exe, (($s.args | ForEach-Object { Format-Arg $_ }) -join ' '))
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $startArgs = @{
            FilePath               = $s.exe
            PassThru               = $true
            NoNewWindow            = $true
            WorkingDirectory       = $repoRoot
            RedirectStandardOutput = (Join-Path $LogDir ($s.id + '.log'))
            RedirectStandardError  = (Join-Path $LogDir ($s.id + '.err.log'))
        }
        if ($s.args.Count -gt 0) { $startArgs.ArgumentList = @($s.args | ForEach-Object { Format-Arg $_ }) }
        $proc = Start-Process @startArgs
        $null = $proc.Handle   # keeps the handle so ExitCode is readable (PS 5.1)
        if ($LowPriority) { try { $proc.PriorityClass = 'BelowNormal' } catch { Write-Warning 'Could not lower priority.' } }

        $code = 0
        $note = ''
        if ($s.id -eq 'e2e') {
            if (-not $proc.WaitForExit([int]($E2eTimeoutMinutes * 60000))) {
                & taskkill.exe /T /F /PID $proc.Id 2>&1 | Out-Null
                $proc.WaitForExit()
                $code = 124
                $note = 'timed out'
                Write-Host ("[{0}] timed out after {1} min; process tree killed." -f $s.id, $E2eTimeoutMinutes)
            }
            else { $code = $proc.ExitCode }
        }
        else {
            $proc.WaitForExit()
            $code = $proc.ExitCode
        }
        $clock.Stop()
        $results += [pscustomobject]@{ id = $s.id; exit = $code; seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1); note = $note }
        if ($code -ne 0) { $exitCode = $code; break }
    }
}
finally {
    Stop-DevHost
    $lines = @('{0,-14} {1,5} {2,9}  {3}' -f 'id', 'exit', 'seconds', '')
    foreach ($r in $results) { $lines += ('{0,-14} {1,5} {2,9}  {3}' -f $r.id, $r.exit, $r.seconds, $r.note) }
    $lines | ForEach-Object { Write-Host $_ }
    $lines | Out-File -FilePath (Join-Path $LogDir 'summary.txt') -Encoding ascii
    Write-Host "Logs: $LogDir"
}
exit $exitCode
