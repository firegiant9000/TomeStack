<#
.SYNOPSIS
  Builds the per-user Velopack installer (ADR-008): self-contained win-x64 publish, then `vpk pack`.
.DESCRIPTION
  The version comes from Directory.Build.props (<Version>) unless -Version is given; see "Versioning" in ADR-008.
  The pack id is 'TomeStack.App', never 'TomeStack': Velopack installs to %LOCALAPPDATA%\<packId> and deletes that folder
  on uninstall, and %LOCALAPPDATA%\TomeStack is the user's data folder.
  The UI bundle must already be built (npm run build --prefix src/Ui). vpk is the repo-local tool (.config/dotnet-tools.json).
  Output: <OutputDir>\<version>\ with TomeStack.App-win-Setup.exe, the full .nupkg and the portable zip. Unsigned (no code
  signing before public release, ADR-008).
.EXAMPLE
  scripts/pack-installer.ps1
.EXAMPLE
  scripts/pack-installer.ps1 -Version 0.1.1 -OutputDir artifacts/installer
#>
param(
  [string] $Version,
  [string] $OutputDir,
  [string] $Project
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) { $OutputDir = Join-Path $root 'artifacts\installer' }
if (-not $Project) { $Project = Join-Path $root 'src\DesktopShell\TomeStack.DesktopShell.csproj' }
if (-not $Version) {
  $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' must be MAJOR.MINOR.PATCH" }

$publish = Join-Path $OutputDir "$Version\publish"
$release = Join-Path $OutputDir $Version
if (Test-Path $release) { Remove-Item $release -Recurse -Force }

dotnet publish $Project -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Push-Location $root   # the tool manifest resolves from the repo
try {
  dotnet tool run vpk -- pack --skip-updates --yes --runtime win-x64 --packId TomeStack.App --packVersion $Version --packTitle TomeStack `
    --packAuthors 'Arlo Kharod' --packDir $publish --mainExe TomeStack.exe --outputDir $release
  if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
} finally { Pop-Location }

Write-Output "Installer: $(Join-Path $release 'TomeStack.App-win-Setup.exe')"
