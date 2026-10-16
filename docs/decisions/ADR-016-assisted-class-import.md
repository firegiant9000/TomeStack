# ADR-016: Assisted class import

Status: accepted (owner decision D34, 2026-10-14; built in Wave 1)
Date: 2026-10-16

## Context

Tester feedback item 11: a detected class page used to end in a class candidate with no effects, so a class had to be rebuilt by hand in the studio. ADR-004 forced every imported effect to `reference`. D34 lets a reviewer confirm the parts of a class that were read from the page, and keeps everything else reference.

## Decision

- **Table reconstruction.** `ClassTableReconstruction` rebuilds a class table from positioned lines: level, proficiency bonus, features, and, for a caster, cantrips, spells known or prepared, and slot columns (or a pact table). It is total (it never throws) and tolerant of rows that wrap. It reproduces the bundled SRD packs cell for cell (`SrdClassTableTests`, which needs the SRD PDFs and skips without them).
- **Proposals, not facts.** `ClassProposals` proposes effects from that table and the class page: hit die, saving throw, armor and weapon proficiencies, a subclass choice (no options), and, for a caster, a `spellcasting` effect. Skills are shown as read, not proposed as a choice. `MulticlassCaster` and `MulticlassTable` are never proposed. The spell list key is the class name, marked unsure until the reviewer saves the candidate.
- **Why the class's own `spellcasting` effect is proposed on the class (R52).** Calculation reads a spellcasting effect from any active revision, the class included. A separate feature carrying it would add a link for no gain.
- **Links.** A class links the features its table names: one feature candidate per distinct name; "Ability Score Improvement" and names ending in "feature" are skipped. A name that matches a detected feature of the job links to it; a name matching installed content (for example "Spellcasting") links to nothing; any other name gets a reference stub, citing the class page, with no effects. The class stores `grantsCandidates` (candidate id and level) and nothing else is added to the stored candidate (no schema version change).
- **Confirmation at accept.** The reviewer ticks the hit die and, when the class has one, the spell slot table. Only ticked effects keep `automatic`. Accept as a draft is held until they are ticked; the UI says why and keeps focus where it was.
- **Build order.** In one transaction the linked features are saved as drafts first (a feature already accepted is pinned to its existing draft; a pending one is built from its candidate; a stub inherits the class's rules families), then the class draft, whose content grants pin each feature's content id and revision id. A blocked feature refuses the class with that feature's codes; an ignored one blocks it ("N linked features are ignored"), though the class can still be accepted as reference.
- **The content grants are automatic, on purpose.** Calculation applies a content grant only when it is automatic; a reference grant would never give the feature. These grants are structural and visible in the list of linked features. The granted features' own effects stay reference, so a linked feature's mechanics still need to be written and published by a person. The grant carries the lowest level at which the table names the feature.
- **Nothing becomes active.** The class and its features are drafts. Publishing is the studio's, with the same validation (ADR-004), and the class and its features can be published together (`content.publishMany`).

## Consequences

- A class page can end in a usable draft after a few ticks, without hand-writing the table.
- Feature stubs add reference-only feature candidates; counts of feature candidates in a book rise accordingly and are not detection recall.
- There is no "un-ignore" command: an ignored linked feature blocks the class draft for good (accept the class as reference, or re-run the import).
- The SRD 5.1 Sorcerer table is not readable by the reconstruction, so it is a features-only candidate there.

## Alternatives considered

- Making every proposal automatic after one tick: rejected; each automatic effect is a claim about the book that a person must see.
- Reference grants for the linked features: rejected; the calculator ignores them.
- A new stored field for the table: rejected; the proposals carry it (R39).

## Evidence

- `tests/ImportWorker.Tests` (reconstruction, proposals, links, stubs), `tests/AppService.Tests/ClassAcceptTests.cs`, `ImportQuarantineTests`, `CandidateReviewTests`.
- `src/Ui/src/components/CandidateReviewPanel.test.tsx` and the e2e flow "imports a fixture class".
