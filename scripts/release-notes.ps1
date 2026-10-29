<#
.SYNOPSIS
  Writes the release notes for one version from docs/CHANGELOG.md (roadmap T5, .github/workflows/release.yml).
.DESCRIPTION
  Takes the "## <version> ..." section of the changelog, up to the next "## " heading, and adds the download and
  verification lines a release page needs. Fails if the section is missing or empty, so a tag cannot publish notes from
  the wrong version.
.EXAMPLE
  scripts/release-notes.ps1 -Version 0.4.0 -Output notes.md
#>
param(
  [Parameter(Mandatory)] [string] $Version,
  [Parameter(Mandatory)] [string] $Output
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$lines = Get-Content (Join-Path $root 'docs\CHANGELOG.md') -Encoding UTF8
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -match "^## $([regex]::Escape($Version))(\s|$)") { $start = $i; break }
}
if ($start -lt 0) { throw "docs/CHANGELOG.md has no '## $Version' section" }
$end = $lines.Count
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -match '^## ') { $end = $i; break }
}
$body = ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
if (-not $body) { throw "The '## $Version' section of docs/CHANGELOG.md is empty" }

$notes = @"
$body

## Download

- **TomeStack.App-win-Setup.exe**: the installer (per-user, no admin rights, Windows 10 and 11, 64-bit). Windows 10 is best-effort.
- **TomeStack.App-win-Portable.zip**: the same build without installing.
- The ``.nupkg`` and ``releases.win.json`` files are Velopack's package and feed. You don't need them; TomeStack never downloads updates by itself.

This build is **unsigned**. See the note at the top about the SmartScreen prompt. Third-party notices are in ``ATTRIBUTION.md`` next to ``TomeStack.exe``.
"@
Set-Content -Path $Output -Value $notes -Encoding UTF8
Write-Output "Release notes for $Version written to $Output ($((Get-Item $Output).Length) bytes)"
