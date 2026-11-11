<#
.SYNOPSIS
  Writes the release notes for one version from docs/CHANGELOG.md (roadmap T5, .github/workflows/release.yml).
.DESCRIPTION
  Takes the "## <version> ..." section of the changelog, up to the next "## " heading, and adds the download and
  verification lines a release page needs. Fails if the section is missing or empty, so a tag cannot publish notes from
  the wrong version.
  -Commit is the full SHA of the commit the tag points to; the verify command in the notes pins it (--source-digest).
.EXAMPLE
  scripts/release-notes.ps1 -Version 0.4.0 -Commit (git rev-parse 'v0.4.0^{commit}') -Output notes.md
#>
param(
  [Parameter(Mandatory)] [string] $Version,
  [Parameter(Mandatory)] [string] $Commit,
  [Parameter(Mandatory)] [string] $Output,
  [string] $ChangelogPath
)
$ErrorActionPreference = 'Stop'

# The release page is not inside the repo, so a link like (decisions/ADR-022.md) in the changelog would resolve against
# github.com/<owner>/<repo>/releases/... and break. Rewrites every relative markdown link target to the tagged file on GitHub.
# Left alone: anything with a scheme (https:, mailto:, ftp:), "#anchor" links, and inline code spans. Images get ?raw=true.
# Targets resolve against docs/ (where CHANGELOG.md lives), so "../README.md" is the repo-root README. Titles are kept.
function Convert-RelativeLinks([string]$Markdown, [string]$Version) {
  $blob = "https://github.com/firegiant9000/TomeStack/blob/v$Version/"
  $pattern = '(?<code>(`+)[^`\n]*?\1)|(?<img>!?)\[(?<text>[^\]\r\n]*)\]\((?<target>[^)\s<>]+)(?<title>\s+"[^"]*")?\)'
  $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
    param($m)
    if ($m.Groups['code'].Success) { return $m.Value }
    $target = $m.Groups['target'].Value
    if ($target.StartsWith('#') -or $target -match '^[A-Za-z][A-Za-z0-9+.-]*:' -or $target.StartsWith('//')) { return $m.Value }
    $suffix = ''
    $cut = $target.IndexOfAny([char[]]'#?')
    if ($cut -ge 0) { $suffix = $target.Substring($cut); $target = $target.Substring(0, $cut) }
    $segments = New-Object System.Collections.Generic.List[string]
    if (-not $target.StartsWith('/')) { $segments.Add('docs') }
    foreach ($part in $target.Split('/')) {
      if ($part -eq '' -or $part -eq '.') { continue }
      if ($part -eq '..') { if ($segments.Count -gt 0) { $segments.RemoveAt($segments.Count - 1) } } else { $segments.Add($part) }
    }
    $url = $blob + ($segments -join '/')
    if ($m.Groups['img'].Value -eq '!') {
      if ($suffix.StartsWith('?')) { $url += $suffix + '&raw=true' } elseif ($suffix) { $url += '?raw=true' + $suffix } else { $url += '?raw=true' }
    } else {
      $url += $suffix
    }
    return $m.Groups['img'].Value + '[' + $m.Groups['text'].Value + '](' + $url + $m.Groups['title'].Value + ')'
  }
  return [regex]::Replace($Markdown, $pattern, $evaluator)
}

if ($Commit -cnotmatch '^[0-9a-f]{40}\z') { throw "Commit '$Commit' must be a full 40-character lower-case commit SHA" }
$root = Split-Path $PSScriptRoot -Parent
if (-not $ChangelogPath) { $ChangelogPath = Join-Path $root 'docs\CHANGELOG.md' }
$lines = Get-Content $ChangelogPath -Encoding UTF8
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
$body = Convert-RelativeLinks $body $Version

$notes = @"
$body

## Download

- **TomeStack.App-win-Setup.exe**: the installer (per-user, no admin rights, Windows 10 and 11, 64-bit). Windows 10 is best-effort.
- **TomeStack.App-win-Portable.zip**: the same build without installing.
- The ``.nupkg`` and ``releases.win.json`` files are Velopack's package and feed. You don't need them; TomeStack never downloads updates by itself.
- **SHA256SUMS**: the SHA-256 hash of each file above. It does not list itself or GitHub's automatic "Source code" archives.

## Verify the download

The release workflow attests every file listed in ``SHA256SUMS``: GitHub records which workflow, tag and commit built it. With the [GitHub CLI](https://cli.github.com/), run this in the folder you downloaded to:

``````
gh attestation verify TomeStack.App-win-Setup.exe --repo firegiant9000/TomeStack --signer-workflow firegiant9000/TomeStack/.github/workflows/release.yml --source-ref refs/tags/v$Version --source-digest $Commit
``````

Without the GitHub CLI, compare ``(Get-FileHash TomeStack.App-win-Setup.exe).Hash`` in PowerShell with the installer's line in ``SHA256SUMS`` (the case of the letters doesn't matter). That check only shows the file arrived intact: ``SHA256SUMS`` comes from this same page and is not attested itself, so the ``gh attestation verify`` command is the one that shows where the file was built (it needs a GitHub CLI recent enough to have ``--source-ref`` and ``--source-digest``, signed in with ``gh auth login``). An attestation is not a code signature, so Windows still shows the SmartScreen prompt.

This build is **unsigned**. See the note at the top about the SmartScreen prompt. Third-party notices are in ``ATTRIBUTION.md`` next to ``TomeStack.exe``.
"@
$parent = Split-Path $Output -Parent
if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
Set-Content -Path $Output -Value $notes -Encoding UTF8
Write-Output "Release notes for $Version written to $Output ($((Get-Item $Output).Length) bytes)"
