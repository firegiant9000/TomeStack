<#
.SYNOPSIS
  Writes the revision ids of the SRD packs a release bundled (down-conversion export, docs/superpowers/specs/2026-10-28-down-conversion-export-spec.md).
.DESCRIPTION
  Reads src/AppService/Content/*.json as they were at <Tag> (git ls-tree, git show), collects the revisionId of every
  revision in those packs, sorts them ordinally and writes one lowercase Guid per line to <Out>. Run it once per release
  cut for the new ladder row (src/AppService/Packages/BundledAt/<release>.txt). Fails if the tag has no packs.
.EXAMPLE
  scripts/bundled-ids-at.ps1 -Tag v0.6.0 -Out src/AppService/Packages/BundledAt/0.6.0.txt
#>
param(
  [Parameter(Mandatory)] [string] $Tag,
  [Parameter(Mandatory)] [string] $Out
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = 'src/AppService/Content/'
$files = @(git -C $root ls-tree --name-only $Tag $folder | Where-Object { $_ -like '*.json' })
if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw "$Tag has no $folder*.json packs" }
$ids = [System.Collections.Generic.HashSet[string]]::new()
foreach ($file in $files) {
  $json = (git -C $root show "${Tag}:$file") -join "`n"
  if ($LASTEXITCODE -ne 0) { throw "git show ${Tag}:$file failed" }
  foreach ($revision in ($json | ConvertFrom-Json).revisions) {
    [void]$ids.Add(([guid]$revision.revisionId).ToString('D'))
  }
}
$sorted = [string[]]$ids
[Array]::Sort($sorted, [System.StringComparer]::Ordinal)
[System.IO.File]::WriteAllText($Out, (($sorted -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
Write-Output "$($sorted.Count) revision ids at $Tag written to $Out"
