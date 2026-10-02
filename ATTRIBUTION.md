# Attribution and third-party notices

TomeStack's own code is licensed under the Apache License 2.0 ([LICENSE](LICENSE), [NOTICE](NOTICE); LIVING_SPECS D07, decided 2026-09-26). Every third-party license below is compatible with it.

## Rules content

TomeStack bundles a slice of two System Reference Documents under the Creative Commons Attribution 4.0 International License (ADR-007; review in [docs/licensing/srd-pack-review.md](docs/licensing/srd-pack-review.md)):

**SRD 5.1:** This work includes material taken from the System Reference Document 5.1 ("SRD 5.1") by Wizards of the Coast LLC and available at https://dnd.wizards.com/resources/systems-reference-document. The SRD 5.1 is licensed under the Creative Commons Attribution 4.0 International License available at https://creativecommons.org/licenses/by/4.0/legalcode.

**SRD 5.2.1:** This work includes material from the System Reference Document 5.2.1 ("SRD 5.2.1") by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

Modified: TomeStack adapted this material into structured data. Selected passages are excerpted or shortened, with line breaks and hyphenation repaired, and rules are encoded as machine-readable effects.

No other third-party rules text is bundled. The test fixtures in `tests/RulesFixtures/` are original to this project (SPEC Q-03) and are not seeded into user data by the shipped app.

## Third-party components in the shipped app

Checked on 2026-09-25, and again on 2026-10-01 for 0.4.0, from `npm ls --omit=dev` and `dotnet list src/DesktopShell package --include-transitive`. Development-only tools (Vite, TypeScript, ESLint, Vitest, xUnit and similar) are not shipped and are not listed.

| Component | Version | License | Copyright |
| --- | --- | --- | --- |
| React (`react`) | 19.3.0 | MIT | Meta Platforms, Inc. and affiliates |
| React DOM (`react-dom`) | 19.3.0 | MIT | Meta Platforms, Inc. and affiliates |
| `scheduler` (dependency of React DOM) | 0.28.0 | MIT | Meta Platforms, Inc. and affiliates |
| Microsoft.Web.WebView2 (SDK) | 1.0.4191.47 | BSD-3-Clause (package `LICENSE.txt`) | Microsoft Corporation |
| Microsoft.Data.Sqlite / .Core | 10.0.12 | MIT | Microsoft Corporation |
| SQLitePCLRaw (`core`, `bundle_e_sqlite3`, `provider.e_sqlite3`, `lib.e_sqlite3`) | 2.1.12 | Apache-2.0 | SourceGear, LLC |
| SQLite (native `e_sqlite3`, via SQLitePCLRaw) | bundled | Public domain | D. Richard Hipp and contributors |
| .NET runtime, WPF (self-contained publish and the installer) | 10.0 | MIT | .NET Foundation and contributors |
| Velopack (library, plus the installer's `Setup.exe` and `Update.exe`) | 1.2.161 | MIT | Caelan Sayler (Velopack). `Setup.exe` and `Update.exe` also contain Rust crates under their own licenses: see `THIRD-PARTY-NOTICES-Velopack.md` |
| PdfPig (`UglyToad.PdfPig` and its `Core`, `Fonts`, `Tokens`, `Tokenization`, `DocumentLayoutAnalysis` and `Package` assemblies), in the import worker (ADR-009) | 0.1.16 | Apache-2.0 | UglyToad and PdfPig contributors |
| Windows SDK C#/WinRT projection (`Microsoft.Windows.SDK.NET.dll`, `WinRT.Runtime.dll`, from `Microsoft.Windows.SDK.NET.Ref`), for Windows OCR in the import worker (ADR-009) | 10.0.19041.57 | **Microsoft Windows SDK license** (Distributable Code; https://aka.ms/WinSDKLicenseURL). Not an open-source license; see below | Microsoft Corporation |

The Microsoft Edge WebView2 **Runtime** is not redistributed. It is a system component that is preinstalled on Windows 11 or installed by the user. The UI uses system fonts and ships no fonts or icon sets.

**Windows SDK Distributable Code (owner decision 2026-09-28, ADR-009):** the two projection assemblies are Microsoft's, under the Windows SDK license, not under Apache-2.0. They are shipped unmodified, and TomeStack's license does not apply to them. The SDK license lets them be distributed as part of a program that adds significant primary functionality, for the Windows platform only. The program must display its own copyright notice, must not alter Microsoft's notices, and must not use Microsoft's trademarks in its name. The license also asks that distributors and end users agree to terms that protect the code at least as much as the SDK license, and that the distributor indemnify Microsoft. **Checked 2026-10-01 (roadmap T5):** both files are on Microsoft's Windows SDK redistribution list, under "Microsoft.Windows.SDK.NET.Ref" (https://learn.microsoft.com/legal/windows-sdk/redist), so shipping them unmodified is allowed. The owner accepted the license's distribution requirements, including the indemnity, on 2026-10-02 ([docs/licensing/release-checklist.md](docs/licensing/release-checklist.md)). The end-user terms they require follow.

### Terms for the Microsoft Windows SDK files

These terms apply to `Microsoft.Windows.SDK.NET.dll` and `WinRT.Runtime.dll`, which ship with TomeStack. They do not apply to the rest of TomeStack.

1. **Licence.** The two files are Microsoft's "Distributable Code", provided under the Microsoft Software License Terms for the Windows Software Development Kit (https://aka.ms/WinSDKLicenseURL). By installing or using TomeStack, you agree that those terms govern your use of the two files.
2. **Restrictions.** You may use the two files only as part of TomeStack, on Windows. You may not:
   - modify them;
   - reverse engineer, decompile or disassemble them, except where the law allows it despite this restriction;
   - remove or change Microsoft's copyright, trademark or patent notices;
   - distribute them on their own, outside TomeStack.
3. **No warranty from Microsoft.** Microsoft provides the files "as is". It gives no warranty and accepts no liability to you for them; the SDK terms set out the details.
4. **Redistributors.** If you pass TomeStack on to others, you must pass these terms on with it. They are in this file, which is installed next to `TomeStack.exe`.

The Velopack installer (ADR-008) installs this file, `LICENSE`, `NOTICE` and `THIRD-PARTY-NOTICES-Velopack.md` next to `TomeStack.exe` (checked by `scripts/installer-smoke.ps1`). BSD-3-Clause, Apache-2.0 and MIT all require the notice to be kept.

**Velopack's `Setup.exe` and `Update.exe`** are compiled from Rust and statically include third-party crates. Velopack publishes no notices for them. `THIRD-PARTY-NOTICES-Velopack.md` (from [docs/licensing/velopack-third-party-notices.md](docs/licensing/velopack-third-party-notices.md)) is generated by `scripts/velopack-notices.mjs` from Velopack's `Cargo.lock` at the pinned tag.
- It lists every crate in the closure for all targets, a superset of what the Windows binaries contain, with each crate's license and license text.
- Every crate is under a permissive license, or offers one: MIT, Apache-2.0, BSD, ISC, Zlib, Unicode-3.0, CDLA-Permissive-2.0, CC0, NCSA, 0BSD or Unlicense. None needs copyleft terms.
- Run the script again whenever the Velopack version changes.
