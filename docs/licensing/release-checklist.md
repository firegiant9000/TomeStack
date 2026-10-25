# Release checklist (ADR-008, roadmap T5)

The four items ADR-008 left open before the first published installer (LIVING_SPECS D08), closed in writing for **0.4.0** on 2026-10-01. Re-check each one for every later release, and record the result here.

| # | Item | Result for 0.4.0 | Result for 0.5.0 (2026-10-12) | Result for 0.6.0 (2026-10-21) |
|---|---|---|---|---|
| 1 | Trademark | **Pass.** No D&D mark in the product | **Pass, with a recorded exception.** The name and branding carry no mark; the importer names "D&D Beyond" only to say which sheet it reads, with a non-affiliation line (owner, 2026-10-12; see [0.5.0 re-check](#050-re-check-2026-10-12)) | **Pass, same exception.** No new kind of use (see [0.6.0 re-check](#060-re-check-2026-10-21)) |
| 2 | Windows SDK binaries (OCR) | **Done.** Allowed to ship; the owner accepted the license's conditions (option (a), 2026-10-02) and the end-user terms are in `ATTRIBUTION.md` | **Unchanged.** Same package and DLLs as 0.4.0 | **Unchanged.** Same package and DLLs as 0.5.0 |
| 3 | Velopack `Setup.exe` / `Update.exe` notices | **Done.** Generated and installed | **Unchanged.** Same Velopack version (1.2.161); the notices file stands | **Unchanged.** Same Velopack version (1.2.161) |
| 4 | Code signing | **Decided: unsigned** (owner, 2026-10-01) | **Unsigned**, as decided | **Unsigned**, as decided |

## Next release re-check (after 0.6.0)

- **Dependencies:** `axe-core` 4.14.0 was added as a development-only dependency (unit tests only, not shipped; `src/Ui/package.json` devDependencies and `package-lock.json`). Licence MPL-2.0, read from the installed package. Nothing ships it, so there is no notice to add; its source is not modified or redistributed.

## 0.6.0 re-check (2026-10-21)

- **Dependencies:** no shipped dependency was added or changed since the `v0.5.0` tag. `src/Ui/package.json`, `src/Ui/package-lock.json`, `.config/dotnet-tools.json`, the workflows and the packing scripts are unchanged, and no package reference in a project file changed (the only project-file change copies a test fixture in `TomeStack.AppService.Tests`).
- **`npm audit`: one high advisory, does not ship.** It is in Vite 8.3.1, a development-only package (`devDependencies`; the app ships the built `dist`, not Vite). Vite 8.3.0 to 8.3.2 bundles a `launch-editor` with three advisories (GHSA-587h-7wmj-h5pm, high; GHSA-698v-4r3x-3vcv and GHSA-r825-w8mm-5pxw, moderate), reachable only through the dev server's open-in-editor endpoint while `npm run dev` is running. It was already present in 0.5.0. Fix: Vite 8.3.3 or later, in a separate development-dependency change after this release.
- **Trademark:** the same search as 0.5.0, over the changes since the `v0.5.0` tag in `src/` (excluding the SRD packs). The product name, window title, installer title, exe metadata and README tagline are unchanged. Every new "D&D Beyond" is one the 0.5.0 re-check already lists for 0.6.0: the layout names in the report, the importer's first step and its family-step hint quoting the override reason, and the texts written into a character made from a D&D Beyond sheet. The text importer and the fillable 2014 sheet write no "D&D Beyond" text. The first step's "TomeStack is not affiliated with D&D Beyond or Wizards of the Coast." stays. No new "Wizards of the Coast", "Dungeons", "fifth edition" or "5e".

## 0.5.0 re-check (2026-10-12)

- **Dependencies:** no shipped dependency was added or changed since 0.4.0. No package reference in a project file changed (the only project-file change embeds the importer's layout maps), and `.config/dotnet-tools.json` is unchanged; the only `package-lock.json` change is `source-map-js` 1.2.1 → 1.2.2, a development-only package.
- **Trademark:** the same search as 0.4.0, over `src/` (excluding build output, dependencies and the SRD packs).
  - The product name, window title, installer title, exe metadata and README tagline are unchanged: "TomeStack", "5E-compatible".
  - **"D&D Beyond" is new in the product,** all of it from the character importer (D16): the sidebar's "Import from D&D Beyond PDF…", the importer's heading, steps and hints, the file picker title, two refusal messages, and two texts written into the characters it creates (the override reason "Imported from D&D Beyond" and the import gap-note text), which therefore also appear in prints and exports. (From 0.6.0 the sidebar entry reads "Import a character (sheet or text)…" (it read "Import a character sheet (PDF)…" before the text entry joined it, R133) because a second layout, the fillable 2014 sheet, uses the same flow; "D&D Beyond" then appears only in the first step's wording that names the export it reads, the not-affiliated line, the family step's hint that quotes the two override reasons, the layout's name in the report of a D&D Beyond sheet, and the three texts written into a character made from a D&D Beyond sheet (the override reason, the differing-number note and the import gap note). The file picker's title ("Character sheet") and the no-form refusal are neutral, and a character made from the fillable sheet gets no "D&D Beyond" text anywhere: "Imported from the character sheet", "The character sheet showed a different number here…" and "Not brought over from the character sheet: …" (tested in `WotcImportTests`). No Wizards logo or mark is used.)
  - **Decided (owner, 2026-10-12): keep it.** Every use names the source of the file being imported, as the Foundry export names `dnd5e`; none uses the mark as TomeStack's name or branding. The importer's first step now says "TomeStack is not affiliated with D&D Beyond or Wizards of the Coast." (tested in `DdbImportPanel.test.tsx`), and the 0.5.0 release notes say the same. This is an owner decision, not legal advice.
  - "Wizards of the Coast" otherwise appears only where it did in 0.4.0 (the SRD attribution and the non-affiliation statements).

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
- To update, the user downloads the newer `Setup.exe` from the release page and runs it. Velopack installs over the old version, the data folder is untouched, and a database upgrade backs it up first (ADR-008 R3). Two copies can result on the first start of a new version: `tomestack.db.v<N>.bak` (the database at the version it was opened with, before a migration) and `tomestack.db.app-<from>-to-<to>.bak` (the database as the previous app version left it, before anything changes). A 0.5.0 folder has no version stamp, so opening it with 0.6.0 leaves `tomestack.db.v10.bak` and `tomestack.db.app-pre-0.6.0-to-0.6.0.bak`; both hold the database as 0.5.0 left it (version 10, before the move to 11).
- The release still carries Velopack's `releases.win.json` and `.nupkg`. An opt-in update check could use them later, but that would need its own decision against ADR-001.

## Release procedure (owner)

1. Merge the release PR. `Directory.Build.props`, the `## <version>` changelog heading and the tag must agree; the workflow checks this. (For 0.6.0 the `<Version>` bump to `0.6.0` is already done on the Wave 2 branch, ruling R130; the owner confirms it in the PR. Tagging and releasing stay the owner's.)
2. Tag and push from an up-to-date `main`: `git tag -a v0.4.0 -m "TomeStack 0.4.0"`, then `git push origin v0.4.0`.
3. The **Release** workflow (`.github/workflows/release.yml`) runs:
   1. the full gate;
   2. the self-contained publish and `vpk pack`;
   3. an install, smoke and uninstall on the runner;
   4. it creates a **draft** release with `Setup.exe`, the portable zip, the `.nupkg`, the Velopack feed files and notes from the changelog.
4. Review the draft on GitHub (assets, notes, the unsigned note), then **Publish**. Nothing is public before that.
5. After publishing: run the upgrade test and the install checks on the published `Setup.exe` (`docs/features/release-0.4.0-verification.md`).
