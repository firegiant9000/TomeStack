# ADR-007: Export package and license policy

Status: proposed. Parts marked **accepted** describe behavior already implemented and tested. D03 and the SRD route are owner decisions that are still open.
Date: 2026-09-25

## Context

SPEC S-01, P-02 and Q-03 require every source to carry license and redistribution metadata, exports to include licensing notices, and the app to ship only licensed material. Packages are used for two different jobs: a **personal backup** (restore on my own machine) and **sharing** (give a character to someone else). The rights rules differ between them. LIVING_SPECS D03 asks what goes into shared packages. The working default is "JSON and licensed assets; omit third-party PDFs".

## Decision

**Accepted (implemented):**

1. Every `SourceRecord` has `license`, `redistributable` and an optional `attribution`. Every package lists a `notices[]` entry for each included source, so attribution travels with the content (`PackageRoundTripTests.Manifest_carries_license_notices_hashes_and_the_attachment_policy`).
2. PDFs and other attachments are never included in a package (`attachmentPolicy` in the manifest).
3. `pdfRef` is machine-local. It is stripped from exports, and an import never changes it. It can contain a local path and the Windows user name.
4. An import never overwrites local license metadata silently. A differing source needs an explicit `keepLocal` or `useImported` choice (`package.source-choice-required`; see the tests in `PackageRoundTripTests`).
5. Imported content never executes code and is never activated without review. Drafts stay drafts (ADR-004).

**Proposed (D03, owner decision needed):**

6. Split export into two explicit purposes, recorded as a new manifest field `purpose`: `backup` or `share` (manifest `formatVersion` 2).
   - **backup:** today's behavior. Everything is included, including sources with `redistributable: false`. The preview and the file name say "personal backup, do not share".
   - **share:** revisions from `redistributable: false` sources are **left out**. The export preview lists every omitted item and the affected characters. On import, the receiver sees `content.missing` diagnostics and the omitted source's title and publisher, so they can obtain it themselves.
   - The alternative, refusing a share export when anything is non-redistributable, is simpler but blocks sharing a character that uses one homebrew note.
7. Until D03 is decided, the current behavior stands: non-redistributable sources are included and flagged `redistributable: false` in `notices[]`.

**Proposed (SRD route, owner decision needed):**

8. SRD 5.1 and SRD 5.2.1 ship as two separate source packs (ADR-002) under CC-BY-4.0. Each `SourceRecord` has `license: "CC-BY-4.0"`, `redistributable: true`, and `attribution` set to the verbatim statement in [licensing/srd-attribution-draft.md](../licensing/srd-attribution-draft.md) once it is approved. No SRD text enters the repository before that approval.

**Blocked (D07):**

9. The license for the project's own code is not chosen, so there is no `LICENSE` file yet. `ATTRIBUTION.md` lists the third-party components that ship.

## Consequences

- Attribution follows content automatically. A shared SRD-based character carries the CC notice.
- A `share` export makes some characters incomplete on the receiving machine. That is intended, and the preview must make it obvious.
- Adding `purpose` is a manifest format change (v2). Older builds refuse v2 packages with a clear message (`package.unsupported-format`).

## Alternatives considered

- **One export kind, always including everything:** today's behavior. It makes it too easy to redistribute third-party homebrew by accident.
- **Stripping non-redistributable text but keeping mechanics:** rejected. The mechanics of a third-party book are part of what may not be redistributed, and the split is hard to explain.

## Evidence

- `tests/AppService.Tests/PackageRoundTripTests.cs` covers notices, the attachment policy, source-choice diffs and `pdfRef` stripping.
- The SRD legal pages were checked on 2026-09-25 (hashes are in the attribution draft).

Supersedes: none. Addresses LIVING_SPECS D03 (proposed) and D07 (blocked).
