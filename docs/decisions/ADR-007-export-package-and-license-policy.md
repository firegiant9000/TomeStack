# ADR-007: Export package and license policy

Status: **accepted** (owner decisions D03, D07 and the SRD route, 2026-09-26; item 10, M2.1, 2026-09-28; item 11, the sheet-export share rule, amended 2026-09-29 for M6 as ADR-011 and ADR-012 require). Items 1–10 are implemented and tested; item 11 is implemented in M6 slice 3 (unmerged). The SRD packs are M1 item 1 (`docs/licensing/srd-pack-review.md`).
Date: 2026-09-25 (proposed), 2026-09-26 (accepted)

## Context

SPEC S-01, P-02 and Q-03 require every source to carry license and redistribution metadata, exports to include licensing notices, and the app to ship only licensed material. Packages are used for two different jobs: a **personal backup** (restore on my own machine) and **sharing** (give a character to someone else). The rights rules differ between them. LIVING_SPECS D03 asks what goes into shared packages. The working default is "JSON and licensed assets; omit third-party PDFs".

## Decision

**Accepted (implemented):**

1. Every `SourceRecord` has `license`, `redistributable` and an optional `attribution`. Every package lists a `notices[]` entry for each included source, so attribution travels with the content (`PackageRoundTripTests.Manifest_carries_license_notices_hashes_and_the_attachment_policy`).
2. PDFs and other attachments are never included in a package (`attachmentPolicy` in the manifest).
3. `pdfRef` is machine-local. It is stripped from exports, and an import never changes it. It can contain a local path and the Windows user name.
4. An import never overwrites local license metadata silently. A differing source needs an explicit `keepLocal` or `useImported` choice (`package.source-choice-required`; see the tests in `PackageRoundTripTests`).
5. Imported content never executes code and is never activated without review. Drafts stay drafts (ADR-004).

**Accepted and implemented (D03, owner, 2026-09-26):**

6. Export has two explicit purposes, recorded in the manifest field `purpose`: `backup` or `share` (manifest `formatVersion` **3**; v2 was already taken by typed effects, ADR-003).
   - **backup** (the default): everything is included, including sources with `redistributable: false`. The file name ends in `-personal-backup.tomestack.zip`, and the UI labels it "Personal backup: includes everything. Do not share it." Pre-import backups are backups.
   - **share:** revisions from `redistributable: false` sources, and those sources, are **left out**. Characters keep their pins. The manifest's `omitted[]` lists each left-out source (title, publisher, license) with its revisions and the characters that pin them. `package.exportPreview` returns the same list before anything is written, and the UI shows it when "Share" is chosen.
   - **Import:** a pin that is neither in the package nor installed is still refused (`package.pin-missing`), unless a v3 share manifest lists it in `omitted[]`. Then it is a warning (`package.content-omitted`) that names the source and publisher. After import, the character shows the content as `content.missing` until the receiver installs the source. v1 and v2 manifests cannot claim to be shares.
   - The alternative, refusing a share export when anything is non-redistributable, is simpler but blocks sharing a character that uses one homebrew note.
   - Evidence: `ExportPurposeTests` (backup keeps everything; share omits and lists; the preview writes nothing; import on a clean machine with the warning and `content.missing`; unlisted pins still refused; older formats cannot claim share; malformed `omitted` rejected; v3 schema), plus the e2e share step.
7. (Superseded by 6.) Before D03, every export included non-redistributable sources.

**Accepted (SRD route, owner, 2026-09-26):**

8. SRD 5.1 and SRD 5.2.1 ship as two separate source packs (ADR-002) under **CC-BY-4.0** (not OGL 1.0a for SRD 5.1). Each `SourceRecord` has `license: "CC-BY-4.0"`, `redistributable: true`, and `attribution` set to the approved statement in [licensing/srd-attribution-draft.md](../licensing/srd-attribution-draft.md) (approved by Arlo Kharod, 2026-09-26), plus a CC-BY §3 `modificationNotice`, which also travels in package `notices[]`. **Implemented (M1 item 1):** `src/AppService/Content/srd-5.1.json` and `srd-5.2.1.json`, seeded into every data folder; checked by `SrdPackTests`.

**Accepted (M2.1, 2026-09-28; amends item 2):**

10. A **full library backup** (package format v6, `scope: "library"`, always `purpose: "backup"`) is a third kind of file, separate from character exports. It is the only package that includes PDF files: the managed copies (`files/<sha256>.pdf`), because losing them is losing the user's data. Character backups and shares still never include PDFs, and a share never includes gap notes. The library backup is personal, like a character backup: the UI says not to share it, and nothing offers to send it anywhere. It carries each source's `attachmentId` and the linked PDF records (a linked path can name the Windows user, which is acceptable in a personal backup). The rules are in [package-format.md](../features/package-format.md#full-library-backup-m21). Evidence: `LibraryBackupTests`. "Share one source's homebrew" is not part of this: homebrew sources are `redistributable: false` by default. (M6 slice 1 later added "Mark as shareable" and source packs, [package-format.md](../features/package-format.md#source-packs-and-mark-as-shareable-m6-slice-1).)

**Accepted (amendment, 2026-09-29, M6 slice 3; as ADR-011 and ADR-012 require before the sheet export model is built; the model and its purpose filter were accepted by the owner in LIVING_SPECS D14 items 4 and 5):**

11. **Sheet exports** (the ADR-011 sheet export model v1, which extension export hooks and ADR-012 adapters read) are a third kind of output, with their own share rule, because a VTT or extension file has no recalculation that could restore what a share leaves out.
    - **`purpose: "share"` is the default.** A source that may be shared (`redistributable: true` and not import-derived: the SRD, homebrew marked as shareable, a received shareable source) is included in full.
    - **Totals only for every other source.** Its content is dropped **whole** (features, resources and their uses and recovery, attacks, spells, toggles and `scales`), not kept without its name, because per-item values are the book's mechanics ("Alternatives" below). The aggregate totals stay: ability scores, Armor Class, hit point maximum, initiative, save and skill totals, proficiency bonus, spell attack bonus and save DC, and slot counts (approved by the owner as built, 2026-09-30). They keep that content's effect, which is the difference from item 6: a character share omits whole revisions and the receiver recalculates without them. The export preview lists what was dropped, by source title and count.
    - **`purpose: "personal"`** additionally includes the full content of **your own** homebrew: a source made on this machine (`origin: "local"`), not import-derived, or one of unknown origin (stored before database v8) only once its author marked it as shareable. Other publishers' `redistributable: false` content, and anything import-derived, is filtered even here, because the file's only use is a tool or server that other people may read. It is labelled as backups are: "Personal copy: includes your own homebrew. Do not share it."
    - **Notices:** every source that contributes anything (full content, or only totals) is listed in the model's `notices[]` (title, publisher, license, attribution, modification notice), and every consumer carries them.
    - **Never, under any purpose:** local paths, attachment ids, gap notes, override reasons, traces or extracted PDF text. The model's schema is an allowlist with no field a path could go in; an output scan (ADR-011) is the second line.
    - Evidence: `ExtensionTests` (M6 slice 3; the sheet export model is `SheetExport.cs`) and `ExportAdapterTests` (M6 slice 4).

**Accepted (D07, owner, 2026-09-26):**

9. The project's own code is Apache-2.0 (`LICENSE`, `NOTICE`). `ATTRIBUTION.md` lists the third-party components that ship.

## Consequences

- Attribution follows content automatically. A shared SRD-based character carries the CC notice.
- A `share` export makes some characters incomplete on the receiving machine. That is intended, and the preview must make it obvious.
- Adding `purpose` is a manifest format change (v3). Older builds refuse v3 packages with a clear message (`package.unsupported-format`). That is intended: they would reject a share package's omitted pins anyway.

## Alternatives considered

- **One export kind, always including everything:** today's behavior. It makes it too easy to redistribute third-party homebrew by accident.
- **Stripping non-redistributable text but keeping mechanics:** rejected. The mechanics of a third-party book are part of what may not be redistributed, and the split is hard to explain.

## Evidence

- `tests/AppService.Tests/PackageRoundTripTests.cs` covers notices, the attachment policy, source-choice diffs and `pdfRef` stripping.
- The SRD legal pages were checked on 2026-09-25 (hashes are in the attribution draft).

Supersedes: none. Resolves LIVING_SPECS D03 and D07.
