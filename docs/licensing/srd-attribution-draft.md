# SRD attribution statements

**Status: APPROVED** by Arlo Kharod on 2026-09-26 (owner decision: CC-BY-4.0 for both SRD 5.1 and SRD 5.2.1, not OGL 1.0a; ADR-007). Every check below is complete, and SRD content may be added. What was added, and from which pages, is recorded in [srd-pack-review.md](srd-pack-review.md). The file keeps its original name so links stay stable.

## Source of the wording (checked 2026-09-25)

The statements below are copied verbatim from the "Legal Information" page of each official SRD PDF:

| SRD | Document | SHA-256 of the file checked |
| --- | --- | --- |
| 5.2.1 | `https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf` (linked from https://www.dndbeyond.com/srd) | `8974902d109d6e63672d7c490bde9ccf052410503d9cfa768237154fbc5e3d87` |
| 5.1 (CC edition) | `https://media.wizards.com/2023/downloads/dnd/SRD_CC_v5.1.pdf` | `2504d2a0abb0a4d491a939be4f17910a2dde0312570ab8d208080225ccf0a1f0` |

The D&D Beyond SRD page lists CC-BY-4.0 **and** OGL 1.0a for SRD 5.1, and CC-BY-4.0 only for SRD 5.2.1. The owner chose CC-BY-4.0 for both.

## Approved statements (verbatim)

**SRD 5.1:**

> This work includes material taken from the System Reference Document 5.1 ("SRD 5.1") by Wizards of the Coast LLC and available at https://dnd.wizards.com/resources/systems-reference-document. The SRD 5.1 is licensed under the Creative Commons Attribution 4.0 International License available at https://creativecommons.org/licenses/by/4.0/legalcode.

**SRD 5.2.1:**

> This work includes material from the System Reference Document 5.2.1 ("SRD 5.2.1") by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

Both documents also say: *do not include any other attribution to Wizards* (5.2.1 adds "or its parent or affiliates"), and a work *may* say that it is "compatible with fifth edition" or "5E compatible".

## Modification notice (CC-BY-4.0 §3(a)(1)(B))

The material is modified (excerpted, reformatted and encoded as data), so each SRD source also carries this notice in `SourceRecord.modificationNotice`, next to the unchanged attribution statement:

> Modified: TomeStack adapted this material into structured data. Selected passages are excerpted or shortened, with line breaks and hyphenation repaired, and rules are encoded as machine-readable effects.

It is kept separate from the attribution so that the attribution stays verbatim, and it names no one, so it adds no "other attribution to Wizards".

## Where the statements appear

1. `ATTRIBUTION.md` and `NOTICE`, which the installer ships next to `TomeStack.exe` (ADR-008).
2. The `attribution` and `modificationNotice` fields of each SRD `SourceRecord`, so both travel into every exported package's `notices[]` (ADR-007). `SrdPackTests` checks that the pack statements match this page exactly.
3. **Still to build (M2):** an in-app About / Sources screen that shows them whenever an SRD pack is installed. Until then, the in-app route to the notices is the exported package and the installed `ATTRIBUTION.md`.

## Checks before approval

- [x] Owner confirms CC-BY-4.0 (not OGL 1.0a) for SRD 5.1 (2026-09-26).
- [x] Approver named: Arlo Kharod · date: 2026-09-26.
- [x] Re-download both PDFs and confirm the wording is unchanged. On 2026-09-26 both files were downloaded again from the URLs above, and their SHA-256 hashes **match** the recorded ones exactly. The legal pages were reread, and both statements above are unchanged.
- [x] Confirm the product name, UI and branding contain no other Wizards attribution or trade dress (SPEC P-03, LIVING_SPECS D08). On 2026-09-26, `src/` (excluding build output and dependencies) had no match for "Wizards", "D&D", "Dungeons", "Beyond" or "Dragon". The UI names the rules families "SRD 5.1 (2014 rules)" and "SRD 5.2.1 (2024 rules)". The SRD source records name no publisher other than through the attribution statement (see `srd-pack-review.md`). The product name trademark check is still D08, before public release.
- [x] CC-BY-4.0 §3 modification wording decided and recorded above.
