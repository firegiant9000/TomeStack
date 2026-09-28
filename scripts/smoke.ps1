<#
.SYNOPSIS
  Runs TomeStack.exe --smoke and checks its JSON report.
.DESCRIPTION
  The smoke loads the bundled UI over the WebView2 virtual host, round-trips app.info and character.list over the
  message bridge, then creates a fixture character, exports it and previews the package (M0 exit gate). It fails
  if the UI attempted any network request. Exit code: 0 pass, 1 fail.
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
  [hashtable] $Environment = @{}
)
$ErrorActionPreference = 'Stop'

$arguments = @('--smoke', '--smoke-report', $Report)
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
Write-Output 'PASS'
exit 0
