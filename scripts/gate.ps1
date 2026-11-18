<#
.SYNOPSIS
Runs the TomeStack gate steps 1-8 (CONTRIBUTING.md, "The gate") in order, with logs, a summary and a watchdog on the DevHost steps.

.DESCRIPTION
Optional helper: the commands in CONTRIBUTING.md stay authoritative. Runs on Windows PowerShell 5.1.
Step ids, in order: npm-ci, lint, ui-test, ui-build, dotnet-build, dotnet-test, e2e, a11y.
The GUI steps (scripts/smoke.ps1 and scripts/single-instance-check.ps1) need the built desktop exe and are left to the owner; they are not in this table.
The run stops at the first failing step; the summary is still printed and written to <LogDir>\summary.txt.
Exit codes: 0 all passed, the failing step's exit code, 124 watchdog timeout (e2e or a11y), 2 bad arguments or a CPU check failure under -RequireQuiet.

.PARAMETER From
Start at this step id and run the later steps.
.PARAMETER Only
Run only these step ids (comma separated).
.PARAMETER LogDir
Where the per-step logs and summary.txt go. Default: a time-stamped folder under the temp directory.
.PARAMETER DevHostStepTimeoutMinutes
Watchdog for the e2e and a11y steps (both use the DevHost); the whole process tree is killed on expiry. Old name: -E2eTimeoutMinutes.
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
    [Alias('E2eTimeoutMinutes')]
    [double]$DevHostStepTimeoutMinutes = 15,
    [int]$UiWorkers,
    [int]$XunitThreads,
    [int]$ProjectParallelism,
    [switch]$LowPriority,
    [int]$CpuWarnPercent = 25,
    [switch]$RequireQuiet,
    [switch]$SkipCpuCheck,
    [switch]$DryRun,
    [switch]$SkipDevHostCleanup,
    [string]$StepsJson,
    [string]$SelfTestMatch
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
# -StepsJson (tests) never touches real processes, so it implies -SkipDevHostCleanup.
$skipCleanup = $SkipDevHostCleanup -or [bool]$StepsJson

# --- Helpers ---
function Format-Arg([string]$a) {
    if ($a.Length -eq 0) { return '""' }
    if ($a -notmatch '[\s"]') { return $a }
    $q = $a -replace '(\\*)"', '$1$1\"'   # backslashes before a quote are doubled, then the quote is escaped
    $q = $q -replace '(\\+)$', '$1$1'      # trailing backslashes are doubled so they do not escape the closing quote
    return '"' + $q + '"'
}

function Test-RepoDevHost([string]$Name, [string]$ExecutablePath, [string]$CommandLine, [string]$Root) {
    # The e2e/a11y harness runs `dotnet <repo>\src\DevHost\bin\...\TomeStack.DevHost.dll`; an apphost TomeStack.DevHost.exe is covered too.
    # Both matches are anchored on the repo root plus a trailing backslash, so a sibling folder such as <root>2 never matches.
    $prefix = $Root.TrimEnd('\') + '\'
    if ($Name -like 'TomeStack.DevHost*.exe' -and $ExecutablePath -and $ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($Name -ieq 'dotnet.exe' -and $CommandLine -and
        $CommandLine.IndexOf($prefix + 'src\DevHost\', [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $CommandLine.IndexOf('TomeStack.DevHost', [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    return $false
}

function Stop-DevHost {
    if ($skipCleanup) { return }
    $count = 0
    try {
        $found = Get-CimInstance Win32_Process | Where-Object { Test-RepoDevHost $_.Name $_.ExecutablePath $_.CommandLine $repoRoot }
        foreach ($p in $found) {
            try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop; $count++ } catch { }
        }
    }
    catch { Write-Warning 'DevHost cleanup unavailable.' }
    Write-Host "DevHost cleanup: stopped $count process(es)."
}

function Write-EarlySummary([string]$Reason) {
    # Bad arguments and a failed CPU check end the run before any step; the reason still goes to summary.txt.
    try {
        New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
        $Reason | Out-File -FilePath (Join-Path $LogDir 'summary.txt') -Encoding ascii
    }
    catch { Write-Warning 'Could not write summary.txt.' }
}

if ($SelfTestMatch) {
    # Tests only: {name, exe, cmd, root} -> prints "match: True|False" without looking at any real process.
    $probe = ConvertFrom-Json -InputObject $SelfTestMatch
    Write-Host ('match: {0}' -f (Test-RepoDevHost ([string]$probe.name) ([string]$probe.exe) ([string]$probe.cmd) ([string]$probe.root)))
    exit 0
}

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
        [pscustomobject]@{ id = 'e2e'; exe = 'npm.cmd'; args = @('run', 'test:e2e', '--prefix', 'src/Ui') },
        [pscustomobject]@{ id = 'a11y'; exe = 'npm.cmd'; args = @('run', 'test:a11y', '--prefix', 'src/Ui') }
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
    $reason = "Unknown step id: {0}. Valid ids: {1}" -f ($bad -join ', '), ($validIds -join ', ')
    Write-Host $reason
    Write-EarlySummary $reason
    exit 2
}
$selected = @($steps)
if ($From) {
    $index = [array]::IndexOf($validIds, $From)
    $selected = @($steps | Select-Object -Skip $index)
}
if ($onlyIds.Count -gt 0) { $selected = @($selected | Where-Object { $onlyIds -contains $_.id }) }

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
            if ($RequireQuiet) { Write-Host "Machine is not quiet. $line"; Write-EarlySummary "Machine is not quiet. $line"; exit 2 }
            Write-Warning "Machine is busy; timings and flakes may not be representative. $line"
        }
        else { Write-Host $line }
    }
    catch {
        if ($RequireQuiet) { Write-Host 'CPU check unavailable and -RequireQuiet was given.'; Write-EarlySummary 'CPU check unavailable and -RequireQuiet was given.'; exit 2 }
        Write-Warning 'CPU check unavailable.'
    }
}

if ($DryRun) { exit 0 }

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
# Both are restored in the finally block, so running the script with & from an open shell leaves that shell as it was.
$originalWorkers = $env:TOMESTACK_TEST_WORKERS
$originalPriority = [Diagnostics.Process]::GetCurrentProcess().PriorityClass
if ($UiWorkers -gt 0) { $env:TOMESTACK_TEST_WORKERS = [string]$UiWorkers }
if ($LowPriority) {
    # Lowering this process first means every step inherits BelowNormal from creation; the per-step set below is a fallback.
    try { [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal' } catch { Write-Warning 'Could not lower priority.' }
}

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
        try { $proc = Start-Process @startArgs }
        catch {
            $clock.Stop()
            Write-Host ("[{0}] failed to start: {1}" -f $s.id, $_.Exception.Message)
            $results += [pscustomobject]@{ id = $s.id; exit = 1; seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1); note = 'failed to start' }
            $exitCode = 1
            break
        }
        $null = $proc.Handle   # keeps the handle so ExitCode is readable (PS 5.1)
        if ($LowPriority) { try { $proc.PriorityClass = 'BelowNormal' } catch { Write-Warning 'Could not lower priority.' } }

        $code = 0
        $note = ''
        if ($s.id -eq 'e2e' -or $s.id -eq 'a11y') {
            if (-not $proc.WaitForExit([int]($DevHostStepTimeoutMinutes * 60000))) {
                & taskkill.exe /T /F /PID $proc.Id 2>&1 | Out-Null
                $proc.WaitForExit()
                $code = 124
                $note = 'timed out'
                Write-Host ("[{0}] timed out after {1} min; process tree killed." -f $s.id, $DevHostStepTimeoutMinutes)
            }
            else { $code = $proc.ExitCode }
        }
        else {
            $proc.WaitForExit()
            $code = $proc.ExitCode
        }
        if ($null -eq $code) { $code = 1 }   # an unreadable exit code is a failure, not a pass
        $clock.Stop()
        $results += [pscustomobject]@{ id = $s.id; exit = $code; seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1); note = $note }
        if ($code -ne 0) { $exitCode = $code; break }
    }
}
finally {
    Stop-DevHost
    $env:TOMESTACK_TEST_WORKERS = $originalWorkers
    if ($LowPriority) { try { [Diagnostics.Process]::GetCurrentProcess().PriorityClass = $originalPriority } catch { Write-Warning 'Could not restore priority.' } }
    $lines = @('{0,-14} {1,5} {2,9}  {3}' -f 'id', 'exit', 'seconds', '')
    foreach ($r in $results) { $lines += ('{0,-14} {1,5} {2,9}  {3}' -f $r.id, $r.exit, $r.seconds, $r.note) }
    $lines | ForEach-Object { Write-Host $_ }
    $lines | Out-File -FilePath (Join-Path $LogDir 'summary.txt') -Encoding ascii
    Write-Host "Logs: $LogDir"
}
exit $exitCode
