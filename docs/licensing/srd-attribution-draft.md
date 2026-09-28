# SRD attribution statements: DRAFT for owner approval

**Status:** draft, not approved. **Blocked on:** the owner's SRD route decision and naming an approver (ADR-007). No SRD content may be added until this page is marked approved with a date and approver.

## Source of the wording (checked 2026-09-25)

The statements below are copied verbatim from the "Legal Information" page of each official SRD PDF:

| SRD | Document | SHA-256 of the file checked |
| --- | --- | --- |
| 5.2.1 | `https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf` (linked from https://www.dndbeyond.com/srd) | `8974902d109d6e63672d7c490bde9ccf052410503d9cfa768237154fbc5e3d87` |
| 5.1 (CC edition) | `https://media.wizards.com/2023/downloads/dnd/SRD_CC_v5.1.pdf` | `2504d2a0abb0a4d491a939be4f17910a2dde0312570ab8d208080225ccf0a1f0` |

The D&D Beyond SRD page lists CC-BY-4.0 **and** OGL 1.0a for SRD 5.1, and CC-BY-4.0 only for SRD 5.2.1. This draft assumes CC-BY-4.0 for both, which the owner must confirm.

## Proposed statements

**SRD 5.1:**

> This work includes material taken from the System Reference Document 5.1 ("SRD 5.1") by Wizards of the Coast LLC and available at https://dnd.wizards.com/resources/systems-reference-document. The SRD 5.1 is licensed under the Creative Commons Attribution 4.0 International License available at https://creativecommons.org/licenses/by/4.0/legalcode.

**SRD 5.2.1:**

> This work includes material from the System Reference Document 5.2.1 ("SRD 5.2.1") by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

Both documents also say: *do not include any other attribution to Wizards* (5.2.1 adds "or its parent or affiliates"), and a work *may* say that it is "compatible with fifth edition" or "5E compatible".

## Where the statements would appear (proposal)

1. `ATTRIBUTION.md` and the installer's notices.
2. An in-app **About / Sources** screen, shown whenever an SRD pack is installed.
3. The `attribution` field of each SRD `SourceRecord`, so the statement travels into every exported package's `notices[]` (ADR-007).

## Checks before approval

- [ ] Owner confirms CC-BY-4.0 (not OGL 1.0a) for SRD 5.1.
- [ ] Approver named: ________ · date: ________
- [ ] Re-download both PDFs and confirm the wording is unchanged (compare the hashes above; a new hash means reread the legal page).
- [ ] Confirm the product name, UI and branding contain no other Wizards attribution or trade dress (SPEC P-03, LIVING_SPECS D08).
- [ ] CC-BY-4.0 §3 also expects an indication of whether the material was modified. Decide the wording for converted or structured SRD data (for example, "adapted into structured data").
