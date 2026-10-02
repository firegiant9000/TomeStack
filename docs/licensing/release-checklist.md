# Release checklist (ADR-008, roadmap T5)

The four items ADR-008 left open before the first published installer (LIVING_SPECS D08), closed in writing for **0.4.0** on 2026-10-01. Re-check each one for every later release, and record the result here.

| # | Item | Result for 0.4.0 |
|---|---|---|
| 1 | Trademark | **Pass.** No D&D mark in the product |
| 2 | Windows SDK binaries (OCR) | **Done.** Allowed to ship; the owner accepted the license's conditions (option (a), 2026-10-02) and the end-user terms are in `ATTRIBUTION.md` |
| 3 | Velopack `Setup.exe` / `Update.exe` notices | **Done.** Generated and installed |
| 4 | Code signing | **Decided: unsigned** (owner, 2026-10-01) |

## 1. Trademark

The rule: TomeStack describes itself as **5E-compatible**. It uses no Wizards of the Coast mark (Dungeons & Dragons, D&D, the ampersand logo) in its name, branding or UI (LIVING_SPECS D09, ADR-012).

A search on 2026-10-01 for `D&D`, `Dungeons`, `Dragons`, `D&D Beyond`, `Wizards of the Coast`, `WotC`, `fifth edition` and `5e` covered the repository (sources, UI, docs, scripts, test goldens), the window title and the build properties. It excluded build output, `node_modules` and the SRD packs.

- **Product (app name, window title, installer title, exe metadata, UI):** no mark. The app is "TomeStack"; the installer title is "TomeStack"; `Product` and `Company` are "TomeStack".
- **README tagline:** said "fifth-edition characters". Changed to "5E-compatible characters" in this release.
- **"Wizards of the Coast"** appears only where it must:
  - in the CC-BY-4.0 attribution for SRD 5.1 and 5.2.1 (`NOTICE`, `ATTRIBUTION.md`, which require the credit);
  - in the non-affiliation statements in the README, the VTT export panel and every Foundry export ("TomeStack is not affiliated with Foundry Gaming LLC, Roll20 or Wizards of the Coast").
- **`dnd5e`** appears only as the id of the Foundry VTT game system an export targets ("Foundry VTT (dnd5e system)", `FoundryDnd5e.cs`, the export goldens). It names the file format, not TomeStack.
- **"D&D Beyond"** appears only in planning docs (SPEC P-03 "no mimicry of D&D Beyond trade dress", MVP, ROADMAP, M3 notes), describing what TomeStack replaces for the owner. It is not in the product.
- **"5e"** appears only in two source comments (`Dice.cs`, `Formulas.cs`), not in the product.

## 2. Windows SDK binaries (OCR)

**What ships:** the import worker (`TomeStack.ImportWorker.Host.exe`, ADR-009) uses Windows OCR through the C#/WinRT projection. So two Microsoft DLLs from the NuGet package `Microsoft.Windows.SDK.NET.Ref` 10.0.19041.57 ship unmodified next to `TomeStack.exe`: `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll`. The OCR engine itself is part of Windows and is not shipped. (The roadmap guessed "likely none"; two DLLs ship.)

**Allowed?** Yes.
- Both files are on Microsoft's redistribution list, in the section "Microsoft.Windows.SDK.NET.Ref": https://learn.microsoft.com/legal/windows-sdk/redist. That section says the package's files "may be distributed unmodified … as part of your program in order to enable your application to call WinRT Apis".
- The package's license is the Windows SDK license (https://aka.ms/WinSDKLicenseURL). Its "Distributable Code" terms permit this.

**The conditions of that license:**

| Condition | Status |
|---|---|
| The program adds significant primary functionality | Met: the DLLs are a small part of the app |
| Ship them unmodified, keep Microsoft's notices, don't use Microsoft's trademarks in the program's name | Met |
| Windows only | Met |
| Display your own copyright notice | Met: `LICENSE` and `NOTICE` |
| **Distributors and end users must agree to terms that protect the code at least as much as the SDK license** | **Met:** "Terms for the Microsoft Windows SDK files" in `ATTRIBUTION.md`, installed next to `TomeStack.exe`, and pointed to from the release notes |
| **The distributor indemnifies Microsoft against claims arising from the distribution or use of the program** | **Accepted by the owner** (2026-10-02) |

**Decided (owner, 2026-10-02): option (a).**
- Keep the DLLs and accept the conditions, including the indemnity.
- Why: it is the standard path for every .NET app that calls Windows APIs through the SDK projection, and the alternatives cost an engineering change (b) or a feature (c).
- The end-user terms are in `ATTRIBUTION.md`, with a pointer to them on every release page (`scripts/release-notes.ps1`).

The options that were considered:
- **(a) Accept them:** keep the DLLs; add short end-user terms covering them (a line in `ATTRIBUTION.md` and on the release page, or an installer licence page); and accept the indemnity.
- **(b) Remove the DLLs:** C#/WinRT can compile the projection into the worker ("embedded" mode, C#/WinRT 1.4.1 and later), so no Microsoft DLL ships. This is an engineering change to the worker, with its own test run, and the build-time license terms still apply.
- **(c) Ship without OCR:** build the worker without the Windows TFM, so nothing from the SDK ships. Scanned PDFs then can't be read.

Note that the roadmap's fallback ("ship with OCR off by default") does not resolve this. With OCR off, the DLLs would still be distributed.

## 3. Velopack `Setup.exe` and `Update.exe` notices

Velopack (MIT, Caelan Sayler) builds `Setup.exe` and `Update.exe` in Rust, with third-party crates linked in, and publishes no notices for them.
- `scripts/velopack-notices.mjs` generates [velopack-third-party-notices.md](velopack-third-party-notices.md) from Velopack's `Cargo.lock` at the pinned tag (1.2.161).
- It lists 545 crates with their licenses and license texts. That is every target; the Windows binaries contain a subset.
- Every crate is permissive or offers a permissive option. There is no MPL and no mandatory copyleft. `r-efi` and `self_cell` also offer LGPL or GPL, but MIT or Apache-2.0 is chosen.
- The file installs next to `TomeStack.exe` as `THIRD-PARTY-NOTICES-Velopack.md` (`installer-smoke.ps1` checks it), and `ATTRIBUTION.md` points to it.
- Regenerate it whenever the Velopack version changes.
- The release workflow refuses to pack when `vpk` and the Velopack library differ.

## 4. Code signing

**Decided (owner, 2026-10-01): unsigned for now.** The options, from Microsoft's documentation (read 2026-10-01):

| Option | Cost | Notes |
|---|---|---|
| Azure Artifact Signing (formerly Trusted Signing) | about $9.99/month | Individuals in the USA or Canada; organizations with 3+ years of tax history. Usable from CI, no hardware token |
| OV certificate from a CA | about $150–500/year | Needs a hardware token or cloud HSM |
| EV certificate | $400+/year | Since 2024 it no longer bypasses SmartScreen; not worth it for that |
| Unsigned | free | What 0.4.0 does |

- **No option removes the SmartScreen warning at first.** A signed app still warns until its publisher builds reputation (weeks, hundreds of installs), but the warning is milder and names a verified publisher. Only the Microsoft Store avoids it, and that needs MSIX packaging instead of Velopack.
- A signature also shows the publisher's verified legal name.
- **What the unsigned build means for users, stated in the release notes:**
  - "Windows protected your PC" appears; choose **More info**, then **Run anyway**.
  - Windows 11 with Smart App Control on, and some managed PCs, block unsigned apps outright.
- **To sign a later release:** `vpk pack` accepts `--azureTrustedSignFile` (Artifact Signing `metadata.json`) or `--signParams` (signtool). Add one to `pack-installer.ps1` and the workflow with secrets, then record the change here.

## Updates

**Decided: a manual update.**
- TomeStack never creates a Velopack `UpdateManager`, so it never checks for, downloads or applies updates (ADR-001: local-only, no network calls; ADR-006: no listening socket).
- To update, the user downloads the newer `Setup.exe` from the release page and runs it. Velopack installs over the old version, the data folder is untouched, and a database upgrade backs it up first (ADR-008 R3).
- The release still carries Velopack's `releases.win.json` and `.nupkg`. An opt-in update check could use them later, but that would need its own decision against ADR-001.

## Release procedure (owner)

1. Merge the release PR. `Directory.Build.props`, the `## <version>` changelog heading and the tag must agree; the workflow checks this.
2. Tag and push from an up-to-date `main`: `git tag -a v0.4.0 -m "TomeStack 0.4.0"`, then `git push origin v0.4.0`.
3. The **Release** workflow (`.github/workflows/release.yml`) runs:
   1. the full gate;
   2. the self-contained publish and `vpk pack`;
   3. an install, smoke and uninstall on the runner;
   4. it creates a **draft** release with `Setup.exe`, the portable zip, the `.nupkg`, the Velopack feed files and notes from the changelog.
4. Review the draft on GitHub (assets, notes, the unsigned note), then **Publish**. Nothing is public before that.
5. After publishing: run the upgrade test and the install checks on the published `Setup.exe` (`docs/features/release-0.4.0-verification.md`).
