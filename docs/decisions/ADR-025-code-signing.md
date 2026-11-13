# ADR-025: Code signing for the installer

Status: **proposed** (research only; the owner decides, no money spent). Supersedes nothing: the 2026-10-01 "unsigned" ruling in [release-checklist.md](../licensing/release-checklist.md#4-code-signing) stands until the owner rules.
Date: 2026-11-11

## Context

The installer `TomeStack.App-win-Setup.exe` is unsigned. Windows shows the SmartScreen "Windows protected your PC" page, and Windows 11 with Smart App Control on blocks it. The build attestation (`gh attestation verify`) proves which workflow built a file, but it is not an Authenticode signature, so Windows ignores it.

Facts from Microsoft's documentation (read 2026-11-11):

- SmartScreen weighs two signals: the publisher's certificate reputation and the file hash reputation. A signed app still warns at first, with the verified publisher name shown. The warning stops after "several weeks and hundreds of clean installs".
- Since 2024 an EV certificate no longer bypasses SmartScreen. Microsoft says paying for EV only to avoid warnings is not justified.
- Unsigned files start at zero reputation with every release. Signed files keep reputation across releases only if the publisher identity stays the same.
- Smart App Control blocks unsigned executables unless they have positive reputation. It checks all executables, not only downloads. The docs do not say how quickly a new signed publisher passes it; we could not confirm that.
- Only the Microsoft Store (MSIX) avoids the warning outright: the Store re-signs the package with a Microsoft certificate, free, worldwide (option 5).
- Every certificate a CA issues names a verified publisher. That is what a signature asserts ("this file comes from this named party, unchanged"), so there is no anonymous public-trust certificate. The two routes that keep a person's legal name off the file are SignPath (the Foundation is the publisher) and the Store (Microsoft signs; the listing shows a chosen publisher display name, and the identity check stays private with Microsoft).

## Options

### 1. SignPath Foundation (free for open source)

- **Eligibility** (signpath.org terms): an OSI-approved licence with no commercial dual licensing (TomeStack is Apache-2.0, so this is met); the signing team owns and maintains the source; actively maintained and already released in the form to be signed; a download page that describes the app; a verifiable reputation for executables; a verifiable automated build, with every release approved by hand; product name and version metadata fixed; MFA for all team members; a "Code signing policy" section on the home page; no exploit or scanning features, a disclosed data flow and an uninstaller. Acceptance is at SignPath's discretion. The `docs/licensing/` review of bundled SRD content and the Windows SDK note may draw questions, since "no proprietary code" is required.
- **Certificate:** issued to **SignPath Foundation**, not to the project or the owner; the private key stays in SignPath's HSM. The terms and home page do not say OV or EV; not confirmed.
- **Cost:** free.
- **Lead time:** not stated anywhere we could read. Unknown.
- **SmartScreen / Smart App Control:** reputation-based, like any CA certificate. The shared publisher name may help or hurt, since reputation is shared with other SignPath Foundation projects; not documented either way.
- **GitHub integration:** the build must run on GitHub-hosted runners and upload an artifact with `actions/upload-artifact` (v4+). The step `signpath/github-action-submit-signing-request` then submits it by artifact id and downloads the signed result. An artifact configuration in SignPath defines what is signed. The docs do not show the manual approval step; per the terms each release needs one.
- **`release.yml` changes:** SignPath cannot be called from `vpk pack` (no signtool or Azure hook). So sign in stages: (1) publish, (2) sign the app exe and DLLs we own through SignPath, (3) `vpk pack` the signed publish folder, (4) sign `TomeStack.App-win-Setup.exe` through SignPath (second request, second approval), then installer smoke, SHA256SUMS and attest as now. The `.nupkg` holds the app files, so they must be signed before the pack. Two upload/submit rounds and approvals per release, with the owner approving in the SignPath UI. `pack-installer.ps1` needs a split into publish and pack steps.

### 2. Azure Artifact Signing (formerly Trusted Signing)

- **Eligibility:** individuals only in the **USA and Canada**; organizations in a longer list of countries (USA, Canada, EU, UK, Australia, New Zealand, Japan, South Korea, Singapore, Switzerland, Norway, Israel). For an individual, identity is taken from the Azure billing account: account type **Individual**, and its legal name and address must match a government ID. That legal name appears on the certificate (a custom CN or O is not possible).
- **Cost:** Microsoft's docs say "about $9.99/month" (Basic tier, 5,000 signatures a month). The pricing page we could fetch showed no figures; confirm in the Azure calculator. Billing is the full monthly amount, not pro-rated. An Azure subscription is needed.
- **Lead time:** Microsoft says "a few business days" for identity validation. Not guaranteed.
- **Certificate:** public trust, short-lived (**three days**); every signature must be timestamped (`http://timestamp.acs.microsoft.com`). Reputation is tied to the verified identity, not to one certificate, so it carries across the rotating certificates.
- **SmartScreen / Smart App Control:** no instant trust; reputation builds over weeks, as with OV. Chains to Microsoft's own trusted root, which Smart App Control accepts; the build-up time for a new publisher is unconfirmed.
- **GitHub integration:** `azure/artifact-signing-action@v2` with `azure/login@v3` and OIDC (`id-token: write`, no stored secret beyond client, tenant and subscription ids). Windows runners only. Roles needed: Certificate Profile Signer.
- **With Velopack:** `vpk pack --azureTrustedSignFile metadata.json` (Velopack 1.3 and later) signs the app binaries and `Setup.exe` itself, each timestamped, and skips files already signed. `metadata.json` holds `Endpoint`, `CodeSigningAccountName` and `CertificateProfileName`; credentials come from the environment (for example the `azure/login` session). The `--signParams` route uses the Microsoft `signtool` with the Artifact Signing dlib and is more setup. The Velopack docs do not say whether the vpk-bundled signer accepts OIDC; test in a dry run.
- **`release.yml` changes:** add `permissions: id-token: write` to `build`; add an `azure/login` step before the pack; pass `-AzureTrustedSignFile` through `scripts/pack-installer.ps1` to `vpk pack`; add three repository variables or secrets. Signing then happens inside the existing pack step, which is where the file's own comment at line 126 expects it, before the installer smoke and SHA256SUMS. The `build` job is `contents: read` only, so the OIDC token is a new permission on a job that runs on tag pushes (guard it as the attest job is guarded). A dry run (`workflow_dispatch`) would also need the login, or the signing step must be skipped there.

### 3. A conventional OV or EV certificate from a CA

OV costs roughly $150 to $300 a year and EV $400 or more (Microsoft's figures). Since June 2023 the private key must sit on a hardware token or an HSM, so a hosted runner cannot hold it. Workarounds are a CA's cloud-HSM signing service (extra cost and its own client tool) or a self-hosted runner with the token plugged in, which does not suit a public repo. EV gives no SmartScreen advantage. Validation takes several business days. Signing would use `vpk pack --signParams` or `--signTemplate`. This is the fallback for an individual who is not accepted by SignPath.

### 4. Stay unsigned (status quo)

Free, no workflow change. Users meet "Windows protected your PC" with "More info, Run anyway" on every release (reputation restarts each time), and Smart App Control and some managed PCs block the installer with no bypass. The release notes already explain this. Attestations remain the only provenance.

### 5. Microsoft Store (MSIX), alongside the unsigned GitHub installer

- **Eligibility and cost:** a free developer account (no registration fee for individuals in the current onboarding), with identity verification; worldwide.
- **Signing:** none by us. The Store re-signs an MSIX after certification. It does not re-sign an EXE or MSI, so the Velopack `Setup.exe` cannot go through the Store unsigned; the Store build would be a separate MSIX package.
- **SmartScreen / Smart App Control:** no warning; Store-signed MSIX packages are trusted by Smart App Control.
- **Lead time:** certification takes "a few business days" for a new app, and each update is certified again.
- **Costs to us, unverified until a spike:** a second packaging path (MSIX, four-part version, `Identity/Publisher` matching Partner Center); Store listing (privacy statement, age rating, screenshots) and Store policy review of an app that imports PDFs and bundles SRD text; updates come from the Store, not Velopack; and **MSIX redirects writes under `%LOCALAPPDATA%` to a per-package location**, so a Store install may not see a library made by the `Setup.exe` install. That last point needs a spike before anything else, since moving a library between the two would need export and import (or a full backup).

## Comparison

| | SignPath Foundation | Azure Artifact Signing | OV/EV from a CA | Unsigned | Store (MSIX) |
|---|---|---|---|---|---|
| Cost | free | about $9.99/month | $150-400+/year | free | free |
| Publisher shown | SignPath Foundation | owner's legal name (individual) | named holder | none | chosen display name |
| Eligibility | OSS, reputation, approval | individual in USA or Canada, or an organization | anyone | any | anyone, verified account |
| Lead time | unknown | days (docs) | days | none | days per release (certification) |
| SmartScreen | reputation builds | reputation builds | reputation builds | warns every release | no warning |
| Smart App Control | passes once trusted (not confirmed how fast) | same | same | blocked | passes |
| CI fit | good, two approvals per release | best, `vpk` built in | poor (hardware key) | n/a | second packaging path, Store updates |

## Consequences

- No option removes the first-weeks warning. All signed options improve it: a named publisher, and Smart App Control becomes passable.
- A signature adds a legal-name publisher (Azure, individual) or a third-party publisher name (SignPath). Choose with that in mind: the Azure route puts the owner's legal name in every installer.
- Whatever is chosen, keep one identity from the first signed release, or reputation restarts.
- The change is confined to `.github/workflows/release.yml`, `scripts/pack-installer.ps1`, [release-checklist.md](../licensing/release-checklist.md) section 4 and the release notes text. No app code changes.
- The signed files must be signed before SHA256SUMS and the attestation, as the workflow already plans. The `gh attestation verify` steps are unchanged.

## Recommendation (for the owner, not a decision)

Apply to SignPath Foundation first: it is free, TomeStack meets the stated licence and build rules, and it needs no identity tied to the owner's legal name. Applying costs nothing and the lead time is unknown, so start now. Azure Artifact Signing is the smoother CI fit (one vpk flag, no per-release approval), but it costs money and names an individual publisher by legal name; the owner ruled it out (below). The free fallback is to stay unsigned and evaluate the Store.

First step: submit the SignPath application (`signpath.org`, Apply), after adding the "Code signing policy" section to the project home page and README that their terms require. In parallel, no spending until SignPath answers.

## Owner rulings (2026-11-11)

1. **No paid signing.** Azure Artifact Signing and a CA certificate are not taken, and no legal name goes on a certificate.
2. **SignPath Foundation first**, applied for now; two manual approvals per release are acceptable.
3. **If SignPath declines, stay unsigned** on GitHub, and evaluate the Microsoft Store (option 5) as a free second channel, starting with the `%LOCALAPPDATA%` spike.
4. **Target: 0.9.0** for the first signed release. 0.8.x stays unsigned.

**Applied 2026-11-12** (the owner submitted the SignPath Foundation application after the README "Code signing policy" section went live). The status stays "proposed" until SignPath answers; the decision is then recorded here and in [release-checklist.md](../licensing/release-checklist.md#4-code-signing) section 4.

## Sources (read 2026-11-11)

- https://signpath.org/terms.html
- https://signpath.org/
- https://docs.signpath.io/trusted-build-systems/github
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options
- https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation
- https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart
- https://learn.microsoft.com/en-us/azure/artifact-signing/faq
- https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations
- https://github.com/azure/artifact-signing-action
- https://azure.microsoft.com/en-us/pricing/details/artifact-signing/ (tiers only, no figures shown)
- https://docs.velopack.io/packaging/signing
- https://learn.microsoft.com/windows/apps/publish/get-started (Store re-signs MSIX; no fee)
- https://learn.microsoft.com/windows/apps/develop/smart-app-control/overview

Not verified: SignPath lead time and certificate type (OV/EV); the exact Artifact Signing price (docs say about $9.99/month); whether `vpk --azureTrustedSignFile` accepts OIDC credentials in CI; how long Smart App Control takes to accept a new signed publisher.
