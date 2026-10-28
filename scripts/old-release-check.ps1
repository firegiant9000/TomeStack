<#
.SYNOPSIS
  Opens golden packages with real older TomeStack releases and checks the calculated sheets (spec section 8, real older build).
.DESCRIPTION
  For each git tag (for example v0.6.0):
  1. Checks the tag out as a detached worktree under -Work (the tag's tree is never edited) and removes it afterwards.
  2. Builds tests/OldReleaseHarness against that tag's own src/AppService with the tag's pinned SDK (its global.json).
  3. For every *.tomestack.zip directly in -Inputs: opens it in a fresh data folder (restore for files named library-backup*,
     import otherwise), then reads each character's sheet and compares it with expected.json in -Expected
     (characters[id].fields; every expected field must be present and equal, extra fields are fine).
  4. Writes an outcome file (JSON, UTF-8 without BOM) to -Out.
  -Inputs, -Expected and -Out may contain {target}, replaced per tag by the tag without its leading v.
  The harness refuses to touch %LOCALAPPDATA%\TomeStack, and nothing here runs an installer or the app.
  Exit code: 0 only if every package of every tag passes, 1 otherwise.
.EXAMPLE
  scripts/old-release-check.ps1 -Tags v0.6.0,v0.5.0 -Inputs tests/RulesFixtures/upgrade/{target} -Expected tests/RulesFixtures/upgrade/{target} -Out $env:TEMP/out/opened-by-{target}.json -Work $env:TEMP/old-release-work
#>
param(
  [Parameter(Mandatory)] [string[]] $Tags,
  [Parameter(Mandatory)] [string] $Inputs,
  [Parameter(Mandatory)] [string] $Expected,
  [Parameter(Mandatory)] [string] $Out,
  [Parameter(Mandatory)] [string] $Work,
  [string] $Repo = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'

function Quote-Arg([string] $a) {
  if ($a.Length -gt 0 -and $a -notmatch '[\s"]') { return $a }
  return '"' + (($a -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}

# Runs a program and captures its streams, so native stderr never trips $ErrorActionPreference on Windows PowerShell 5.1.
$BuildTimeoutMs = 20 * 60 * 1000
$RunTimeoutMs = 5 * 60 * 1000

function Stop-Tree([System.Diagnostics.Process] $p) {
  try {
    $k = [System.Diagnostics.Process]::Start((New-Object System.Diagnostics.ProcessStartInfo -Property @{
      FileName = 'taskkill'; Arguments = "/T /F /PID $($p.Id)"; UseShellExecute = $false; CreateNoWindow = $true
      RedirectStandardOutput = $true; RedirectStandardError = $true }))
    $k.StandardOutput.ReadToEnd() | Out-Null
    $k.WaitForExit(30000) | Out-Null
  } catch { }
  try { if (-not $p.HasExited) { $p.Kill() } } catch { }
}

function Invoke-Native([string] $File, [string[]] $Arguments, [string] $WorkingDirectory, [int] $TimeoutMs = $RunTimeoutMs) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $File
  $psi.Arguments = (($Arguments | ForEach-Object { Quote-Arg $_ }) -join ' ')
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.StandardOutputEncoding = [Text.Encoding]::UTF8
  $psi.StandardErrorEncoding = [Text.Encoding]::UTF8
  $psi.CreateNoWindow = $true
  if ($WorkingDirectory) { $psi.WorkingDirectory = $WorkingDirectory }
  $p = [System.Diagnostics.Process]::Start($psi)
  $outTask = $p.StandardOutput.ReadToEndAsync()
  $errTask = $p.StandardError.ReadToEndAsync()
  $timedOut = -not $p.WaitForExit($TimeoutMs)
  if ($timedOut) { Stop-Tree $p; $p.WaitForExit(30000) | Out-Null }
  $stdout = ''
  $stderr = ''
  if ($outTask.Wait(10000)) { $stdout = $outTask.Result }
  if ($errTask.Wait(10000)) { $stderr = $errTask.Result }
  $code = -1
  if ($p.HasExited) { $code = $p.ExitCode }
  return [pscustomobject]@{ ExitCode = $code; Out = $stdout; Err = $stderr; TimedOut = $timedOut }
}

function Invoke-Git([string[]] $Arguments) {
  return Invoke-Native 'git' (@('-C', $script:Repo) + $Arguments) $null
}

# Resolves against PowerShell's $PWD, not the process working directory that [IO.Path] would use.
function Get-FullPath([string] $Path) {
  return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Is-Under([string] $Path, [string] $Root) {
  $p = (Get-FullPath $Path).TrimEnd('\', '/') + '\'
  $r = (Get-FullPath $Root).TrimEnd('\', '/') + '\'
  return $p.StartsWith($r, [StringComparison]::OrdinalIgnoreCase)
}

function Is-Number($v) {
  return ($v -is [int]) -or ($v -is [long]) -or ($v -is [double]) -or ($v -is [decimal]) -or ($v -is [single])
}

function Json-Text($v) {
  return (ConvertTo-Json -InputObject $v -Compress -Depth 20)
}

function Values-Equal($a, $b) {
  if ((Is-Number $a) -and (Is-Number $b)) { return ([double] $a) -eq ([double] $b) }
  return (Json-Text $a) -ceq (Json-Text $b)
}

function Short([string] $s, [int] $max) {
  if ($s.Length -le $max) { return $s }
  return $s.Substring(0, $max) + '...'
}

function Write-Utf8NoBom([string] $Path, [string] $Text) {
  $dir = Split-Path -Parent $Path
  if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

# --- validate arguments ---
$tagList = @()
foreach ($t in $Tags) { foreach ($piece in ($t -split ',')) { if ($piece.Trim()) { $tagList += $piece.Trim() } } }
if ($tagList.Count -eq 0) { throw 'no tags given' }
foreach ($tag in $tagList) {
  if ($tag -cnotmatch '^v\d+\.\d+\.\d+$') { throw "tag '$tag' is not of the form vMAJOR.MINOR.PATCH" }
}
if ($tagList.Count -gt 1 -and $Out -notlike '*{target}*') { throw 'more than one tag needs {target} in -Out' }

$Repo = Get-FullPath $Repo
$top = Invoke-Git @('rev-parse', '--show-toplevel')
if ($top.ExitCode -ne 0) { throw "not a git repository: $Repo" }
$repoTop = Get-FullPath ($top.Out.Trim())
$Work = Get-FullPath $Work
if (Is-Under $Work $repoTop) { throw "-Work must not be inside the repository ($repoTop)" }
if (-not (Test-Path -LiteralPath $Work)) { New-Item -ItemType Directory -Path $Work | Out-Null }

$harnessDir = Join-Path $repoTop 'tests/OldReleaseHarness'
foreach ($f in @('OldReleaseHarness.csproj', 'Program.cs')) {
  if (-not (Test-Path -LiteralPath (Join-Path $harnessDir $f))) { throw "missing $harnessDir/$f" }
}

$anyFailure = $false
$removalFailed = $false

foreach ($tag in $tagList) {
  $target = $tag.Substring(1)
  $inputsDir = Get-FullPath ($Inputs.Replace('{target}', $target))
  $expectedDir = Get-FullPath ($Expected.Replace('{target}', $target))
  $outFile = Get-FullPath ($Out.Replace('{target}', $target))

  if (-not (Test-Path -LiteralPath $inputsDir -PathType Container)) { throw "inputs folder not found: $inputsDir" }
  $expectedFile = Join-Path $expectedDir 'expected.json'
  if (-not (Test-Path -LiteralPath $expectedFile)) { throw "expected.json not found: $expectedFile" }
  [string[]] $names = @(Get-ChildItem -LiteralPath $inputsDir -File -Filter '*.tomestack.zip' | Where-Object { $_.Name.EndsWith('.tomestack.zip', [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.Name })
  [Array]::Sort($names, [StringComparer]::Ordinal)
  if ($names.Count -eq 0) { throw "no *.tomestack.zip in $inputsDir" }
  $expectedDoc = [IO.File]::ReadAllText($expectedFile) | ConvertFrom-Json

  $worktree = Join-Path $Work $tag
  $buildDir = Join-Path $Work "build-$tag"
  $binDir = Join-Path $Work "bin-$tag"
  $results = @()
  $tagCommit = ''
  $worktreeAdded = $false

  try {
    if (Test-Path -LiteralPath $worktree) {
      Invoke-Git @('worktree', 'remove', '--force', $worktree) | Out-Null
      if (Test-Path -LiteralPath $worktree) { Remove-Item -LiteralPath $worktree -Recurse -Force }
    }
    foreach ($stale in @($buildDir, $binDir)) {
      if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
    }

    # a. the tag as a detached worktree
    $add = Invoke-Git @('worktree', 'add', '--detach', $worktree, $tag)
    if ($add.ExitCode -ne 0) { throw "git worktree add $tag failed: $($add.Err.Trim())" }
    $worktreeAdded = $true
    $rev = Invoke-Git @('rev-parse', "$tag^{commit}")
    if ($rev.ExitCode -ne 0) { throw "cannot resolve $tag" }
    $tagCommit = $rev.Out.Trim()

    # b. build the harness against the tag's own AppService, with the tag's pinned SDK
    New-Item -ItemType Directory -Path $buildDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $harnessDir 'OldReleaseHarness.csproj') -Destination $buildDir
    Copy-Item -LiteralPath (Join-Path $harnessDir 'Program.cs') -Destination $buildDir
    $tagGlobal = Join-Path $worktree 'global.json'
    if (Test-Path -LiteralPath $tagGlobal) { Copy-Item -LiteralPath $tagGlobal -Destination $buildDir }
    $appService = (Join-Path $worktree 'src/AppService/TomeStack.AppService.csproj').Replace('\', '/')
    $buildError = $null
    Push-Location $buildDir
    try {
      # Global properties reach the tag's own projects too (their Directory.Build.props set warnings-as-errors); the tag tree stays unedited.
      # No node reuse or shared compiler server, so nothing holds the worktree open when it is removed.
      $build = Invoke-Native 'dotnet' @('build', 'OldReleaseHarness.csproj', '-c', 'Release', '-nologo', "-p:AppServicePath=$appService", '-p:TreatWarningsAsErrors=false', '-p:NuGetAudit=false', '-nodeReuse:false', '-p:UseSharedCompilation=false', '-o', $binDir) $buildDir $BuildTimeoutMs
    }
    finally { Pop-Location }
    $harnessDll = Join-Path $binDir 'OldReleaseHarness.dll'
    if ($build.TimedOut) {
      $buildError = "harness broken: ${tag}: dotnet build timed out after 20 minutes"
    }
    elseif ($build.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $harnessDll)) {
      $first = ($build.Out + "`n" + $build.Err) -split "`r?`n" | Where-Object { $_ -match '\berror\s+[A-Z]+\d+' } | Select-Object -First 1
      if (-not $first) { $first = "dotnet build exited $($build.ExitCode)" }
      $buildError = "harness broken: ${tag}: $($first.Trim())"
    }

    if ($buildError) {
      foreach ($n in $names) { $results += [ordered]@{ package = $n; status = 'harness-broken'; detail = $buildError } }
    }
    else {
      # c. each package in a fresh data folder
      $index = 0
      foreach ($n in $names) {
        $index++
        $dataDir = Join-Path $Work "data-$tag-$index"
        if (Test-Path -LiteralPath $dataDir) { Remove-Item -LiteralPath $dataDir -Recurse -Force }
        $status = 'pass'
        $detail = ''
        $mode = 'import'
        if ($n.StartsWith('library-backup', [StringComparison]::Ordinal)) { $mode = 'restore' }
        $run = Invoke-Native 'dotnet' @($harnessDll, $mode, (Join-Path $inputsDir $n), '--data-dir', $dataDir) $Work $RunTimeoutMs
        $doc = $null
        try { $doc = $run.Out | ConvertFrom-Json } catch { $doc = $null }
        if ($run.TimedOut) {
          $status = 'harness-broken'
          $detail = "harness broken: ${tag}: $mode timed out after 5 minutes"
        }
        elseif ($run.ExitCode -ge 2 -or $run.ExitCode -lt 0 -or $null -eq $doc) {
          $line = ($run.Err -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
          if (-not $line) { $line = "exit $($run.ExitCode), unparsable output" }
          $status = 'harness-broken'
          $detail = "harness broken: ${tag}: $($line.Trim())"
        }
        elseif ($run.ExitCode -eq 1 -or -not $doc.ok) {
          $status = 'fail'
          $detail = 'dispatcher refused: ' + (Short (Json-Text $doc.error) 300)
        }
        else {
          $diffs = @()
          foreach ($id in @($doc.characters)) {
            $sheetRun = Invoke-Native 'dotnet' @($harnessDll, 'sheet', [string] $id, '--data-dir', $dataDir) $Work $RunTimeoutMs
            $sheet = $null
            try { $sheet = $sheetRun.Out | ConvertFrom-Json } catch { $sheet = $null }
            if ($sheetRun.TimedOut) {
              $status = 'harness-broken'
              $detail = "harness broken: ${tag}: sheet timed out after 5 minutes"
              break
            }
            if ($sheetRun.ExitCode -ge 2 -or $null -eq $sheet) {
              $line = ($sheetRun.Err -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
              if (-not $line) { $line = "exit $($sheetRun.ExitCode), unparsable output" }
              $status = 'harness-broken'
              $detail = "harness broken: ${tag}: $($line.Trim())"
              break
            }
            if ($sheetRun.ExitCode -eq 1) { $diffs += "${id}: sheet refused"; continue }
            $expChar = $null
            if ($expectedDoc.characters) { $expChar = $expectedDoc.characters.PSObject.Properties[[string] $id] }
            if ($null -eq $expChar -or $null -eq $expChar.Value.fields) { $diffs += "${id}: no expectation in expected.json"; continue }
            foreach ($prop in $expChar.Value.fields.PSObject.Properties) {
              $got = $sheet.fields.PSObject.Properties[$prop.Name]
              if ($null -eq $got) { $diffs += "$($prop.Name): expected $(Json-Text $prop.Value), got <missing>" }
              elseif (-not (Values-Equal $prop.Value $got.Value)) { $diffs += "$($prop.Name): expected $(Json-Text $prop.Value), got $(Json-Text $got.Value)" }
            }
          }
          if ($status -eq 'pass' -and $diffs.Count -gt 0) {
            $status = 'fail'
            $detail = (@($diffs | Select-Object -First 5) -join '; ')
            if ($diffs.Count -gt 5) { $detail += "; and $($diffs.Count - 5) more" }
          }
        }
        $results += [ordered]@{ package = $n; status = $status; detail = $detail }
      }
    }

    # e. the outcome file
    $inputEntries = @()
    $inTop = Invoke-Native 'git' @('-C', $inputsDir, 'rev-parse', '--show-toplevel') $null
    if ($inTop.ExitCode -ne 0) { throw "inputs folder is not inside a git repository: $inputsDir" }
    $inRoot = (Get-FullPath ($inTop.Out.Trim())).TrimEnd('\', '/') + '\'
    $hashed = @()
    foreach ($n in $names) { $hashed += (Join-Path $inputsDir $n) }
    $hashed += $expectedFile
    foreach ($file in $hashed) {
      $full = Get-FullPath $file
      if (-not $full.StartsWith($inRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "input outside its git toplevel: $full" }
      $inputEntries += [ordered]@{
        path   = $full.Substring($inRoot.Length).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
      }
    }
    $outcome = [ordered]@{
      harnessVersion = 1
      tag            = $tag
      tagCommit      = $tagCommit
      inputs         = @($inputEntries)
      results        = @($results)
    }
    Write-Utf8NoBom $outFile ((ConvertTo-Json -InputObject $outcome -Depth 6) + "`n")

    $pass = @($results | Where-Object { $_.status -eq 'pass' }).Count
    $fail = @($results | Where-Object { $_.status -eq 'fail' }).Count
    $broken = @($results | Where-Object { $_.status -eq 'harness-broken' }).Count
    Write-Host "$tag ($($tagCommit.Substring(0, 7))): $pass pass, $fail fail, $broken harness-broken -> $outFile"
    if ($pass -ne $results.Count) { $anyFailure = $true }
  }
  finally {
    # f. never leave a tag checked out
    if ($worktreeAdded -or (Test-Path -LiteralPath $worktree)) {
      Invoke-Git @('worktree', 'remove', '--force', $worktree) | Out-Null
      if (Test-Path -LiteralPath $worktree) {
        # A lingering build server or compiler node can hold files open: stop them and try once more.
        Invoke-Native 'dotnet' @('build-server', 'shutdown') $null $RunTimeoutMs | Out-Null
        Start-Sleep -Seconds 2
        Invoke-Git @('worktree', 'remove', '--force', $worktree) | Out-Null
      }
      if (Test-Path -LiteralPath $worktree) {
        Write-Warning "could not remove the worktree $worktree; remove it by hand (git worktree remove --force)"
        $removalFailed = $true
      }
    }
    Invoke-Git @('worktree', 'prune') | Out-Null
  }
}

if ($removalFailed) { exit 1 }
if ($anyFailure) { exit 1 }
exit 0
