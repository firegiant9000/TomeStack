# ADR-007: Export package and license policy

Status: **accepted** (owner decisions D03, D07 and the SRD route, 2026-09-26). Items 1–9 are implemented and tested. The SRD packs are M1 item 1 (`docs/licensing/srd-pack-review.md`).
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
