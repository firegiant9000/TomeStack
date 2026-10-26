# Security

## Reporting a vulnerability

Report it privately on GitHub: open the repository's **Security** tab and choose **Report a vulnerability** ([new report](https://github.com/firegiant9000/TomeStack/security/advisories/new)). Do not open a public issue, and do not send it by email. Include the TomeStack version (Settings > About), the Windows version and the steps to reproduce.

## Supported versions

Only the latest release on the [Releases page](https://github.com/firegiant9000/TomeStack/releases) gets fixes. A fix ships as a new release; older versions are not patched.

## What a release is

- The **Release** workflow (`.github/workflows/release.yml`) builds every release on GitHub's runners when a version tag is pushed. It runs the full gate, packs the installer and creates a draft release; the owner reviews the draft and publishes it.
- The release lists a `SHA256SUMS` file with the SHA-256 hash of each file in it.
- Each file listed in `SHA256SUMS` has a build attestation. It proves which workflow, in this repository, built the file. It does not make the build reproducible byte for byte.
- Releases are **unsigned** until code signing is added, so Windows SmartScreen warns the first time you run `TomeStack.App-win-Setup.exe` ("Windows protected your PC": choose **More info**, then **Run anyway**).

## Verify a download

Put the downloaded file and `SHA256SUMS` in one folder and run, in PowerShell:

```powershell
$want = ((Select-String -Path .\SHA256SUMS -Pattern 'TomeStack.App-win-Setup.exe' -SimpleMatch).Line -split '\s+')[0]
(Get-FileHash .\TomeStack.App-win-Setup.exe -Algorithm SHA256).Hash -eq $want
```

`True` means the file matches the hash the release published. To check that this repository's release workflow built it, use the [GitHub CLI](https://cli.github.com/):

```powershell
gh attestation verify .\TomeStack.App-win-Setup.exe --repo firegiant9000/TomeStack --signer-workflow firegiant9000/TomeStack/.github/workflows/release.yml
```

To also check the tag it was built from, add `--source-ref refs/tags/v<version>` (for example `refs/tags/v0.7.1`). For another file, put its name in place of `TomeStack.App-win-Setup.exe`.

## What the app does on the network

Nothing, in normal use ([ADR-001](docs/decisions/ADR-001-local-only-windows-release.md)):

- There is no account, telemetry or update check. To update, download the newer release and run its installer.
- The WebView2 host refuses every request outside the app's virtual host, and the app opens no listening socket ([ADR-006](docs/decisions/ADR-006-desktop-host-and-ipc.md)).
- Your library stays in the local data folder (`%LOCALAPPDATA%\TomeStack` by default).
