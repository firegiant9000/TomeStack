# ADR-023: Save for an older TomeStack (down-conversion export)

Status: **accepted** (owner, 2026-10-28: answers Q1–Q14, LIVING_SPECS D83 and D85; numbered ADR-023 by the owner, 2026-11-03, because ADR-022 is reserved for the D76 content v12 engine work). Built and merged to `main` (#110, #111, #112, #124, #127, #131).
Date: 2026-11-03

## Context

A player on the current TomeStack sends a character, a source pack or a campaign pack to a friend who has not updated. Until now the friend's build refused the file (`package.schema-unsupported`) and the sender learnt why only from the friend. The version-compat findings (O4) first said "no down-conversion"; the owner reversed that on 2026-10-28.

What the investigation found:

- **The package format never moved.** Every release from 0.4.0 on writes format 5, 7, 8 or 9 and reads up to 9, so `package.unsupported-format` cannot fire between them. The whole problem is two schema ladders: character (v7 to v11) and content (v9 to v12).
- **There are five rungs for packages:** 0.4.0 (character ≤ 7, content ≤ 9, gap note v1), 0.5.0 (character ≤ 8, content ≤ 9), 0.6.0 (character ≤ 9, content ≤ 10), 0.7.x (character ≤ 10, content ≤ 11) and 0.8.x (character ≤ 11, content ≤ 12; added by the 0.8.0 release PR). Patch releases share their minor's rung. Gap notes are v2 from 0.5.0 on, so no target needs a gap-note step.
- **Bundled SRD content is part of the rung.** An older build refuses any revision it did not ship under a bundled source (`package.bundled-source-content`), and character packages carry the bundled revisions they pin.
- **Published revisions are insert-only.** A lowered revision cannot keep its id (`package.revision-conflict`), and a copy with a new id is refused on the maker's own machine (`package.own-source`) unless something maps it back.

## Decision

1. **One release ladder** (`src/AppService/Packages/ReleaseLadder.cs`), released rows only: for each minor release, the highest character, content, gap-note, package-format and database schema it reads, its bundled SRD revision ids (`Packages/BundledAt/<release>.txt`, made by `scripts/bundled-ids-at.ps1` in each minor's release PR) and whether it is offered as a target. The current build's maxima come from the schema constants, not from a row. O1 (D83) reads the same table: every preview returns `opensIn` (the first row that opens the file, or `null`) and `opensInCauses[]`, and `package.needs-newer-build` fires only when that is newer than the previous minor or `null`.
2. **Target: the previous minor only** (R4, D103; under Q1 it was 0.7.0, 0.6.0 and 0.5.0). Today that is 0.8.0, offered from 0.9.0: a build never lists its own minor, so a 0.8.1 build offers nothing until the 0.9.0 row is added. Rows for earlier releases keep their place in the ladder for re-pin and "opens in", are not offered, and their lowering steps stay in `RulesCore`. Not 0.4.0, which opens no character package from 0.5.0 on. `package.targets` lists the offered rows; a newly offered rung first needs its real-build proof (item 16's harness: goldens under `for-<T>/`, a committed outcome and the weekly job's tag list). The goldens and outcomes of the rungs no longer offered were deleted.
3. **The converter lives in `RulesCore`** (`DownConvert.Below`, one step per version: content 12 → 11 → 10 → 9, character 11 → 10 → 9 → 8) as pure functions that return the lowered record and a list of dropped, blocking and silent changes, and validate the copy at the target version (anything still refused blocks, `below.validation-error`). Ids, hashes, manifests, pins, cascade, bundled sets and file names live in `AppService/Packages/DownConversion.cs`.
4. **Never silently stronger** (Q2): a field whose loss makes the sheet weaker or by hand is dropped and listed; one whose loss would lift a cost, a limit, a gate, a penalty or a vulnerability blocks the export. Judged by value where a field can go either way. A running encounter blocks (Q3).
5. **The sender confirms what they saw.** The preview returns `changes[]` (`dropped`, `blocks`, `copied`, `leftOut`) and `changesHash`; every save with a target must present the same hash (`package.target-changed`) and is refused while anything blocks (`package.target-blocked`). The tick is required only when something is dropped or left out (Q7).
6. **New ids, hashed.** A changed revision becomes a copy with id UUIDv5(a fixed TomeStack namespace, `<root original>:<copy schema>:<SHA-256 of the copy's canonical bytes>`) and a `downConvertedFrom { revisionId, schemaVersion }` key naming the root original. The root is the input's own verified marker when the input is already a copy, so a copy of a copy collapses to the original.
7. **Cascade.** Only `grant.content` and `choice.options` hold revision ids; a revision that grants or offers a copy is rewritten and copied too (listed as `copied`). Content-id references are never rewritten.
8. **Bundled revisions are never lowered or copied** (Q9): one in the target's set is written as it is; a pin to one outside it blocks.
9. **What blocks the file.** In a character package every blocker blocks. In a source pack, campaign pack or library backup a blocker in a superseded revision or a draft (bundled ones never travel) that no character pins and no written revision grants or offers leaves that revision out with a `leftOut` line; anything else blocks. Two rules from content 13 refine this.
    - **Plain gear becomes a custom item.** An equipment entry whose item is a bundled revision the target release does not ship no longer blocks when the item is plain gear: no effects, or only an `item` effect with no charges and no attunement. A pack (an `item` effect with `contains`) counts as plain gear. It becomes one custom item named after the pack, and its contents are not expanded. The custom item keeps the name and the quantity, has no note and is not equipped, and the preview lists it as dropped (`below.gear-as-custom-item`). Its id is a UUIDv5 of the entry id, so a preview and the export agree. Weapons, armour, mods and anything with effects, charges or attunement still block. A character cannot hold more than 50 custom items. If its existing custom items plus the converted ones would pass 50, or a quantity or name cannot be kept, nothing is converted and the save blocks with one line that says why. The rule applies only when the target's character format has custom items, which is character schema 9 or later. Below that, plain gear blocks as before. An item that anything else still holds keeps blocking: a kept entry or its mods, another character in the same file, or a revision in the file that grants or offers it.
    - **Creation guides.** A creation guide is left out below content 13. A source or campaign file whose source is left with nothing after that is blocked (`package.target-blocked`), with the same words the reader uses for an empty source.
10. **The manifest says `madeFor`, with no format bump** (Q6): a bump would make the file refused by exactly the build it is for. `appVersion` stays the true maker. The file name ends `-for-<release>`.
11. **The way back is guarded** (Q4). On import or restore, a closure of copies (copies linked by grants and options) is re-pinned to the originals only if every member verifies: its id matches its own bytes and marker, the original is here, published, not bundled, at the marker's schema, with the same content and source, and lowering the originals again gives exactly the file's copy ids. It is all or nothing; otherwise every member imports as an ordinary revision with `package.down-converted`. A malformed marker or `madeFor` is ignored with `package.marker-ignored`, never an exception. The re-pin runs before the pin-missing and own-source checks.
12. **A copy never counts as newest** while an unmarked published revision of its content exists (Q14): for update offers, the content list, `pack.newest-changes` and `restore.newest-changes`, and (C2-Q1) at every other site where "newest" is the revision a user acts on: the studio list, publish-many, spell lists, coverage, the sandbox, import-candidate linking, and, through `IContentCatalog.NewestPublished`, an unpinned cast spell, the export carrying it and the validator. RulesCore cannot check a marker, so the catalog's default is stored order and the app's store applies the rule.
13. **Editing a copy strips its marker:** `content.saveDraft` and `content.publish` remove `downConvertedFrom`, so a revision published from a copy is ordinary.
14. **A lowered character that would replace a fuller local one** is warned about (`package.down-converted-character`) with what the local copy has that the file lacks; the pre-import backup is taken as usual.
15. **A full library backup can be lowered too** (Q5, against the recommendation). Drafts are lowered and stay drafts (Q13), `revisionOrder` lists the copies in their originals' places, and the Backups screen says to restore it into a new, empty data folder on the older version.
16. **Real-build proof** (Q8): a committed harness (`tests/OldReleaseHarness/`, `scripts/old-release-check.ps1`) opens every golden copy in the real previous-minor build (v0.8.0 now; `outcomes/0.8.0.json`, 5 pass); the weekly job runs it, and the PR gate checks that the committed outcomes match the goldens' hashes.

## Consequences

- A sender can reach a friend on the previous minor (0.8.0 from 0.9.0) without either of them touching a data folder, and sees exactly what the copy loses before the Save dialog opens.
- The round trip is lossless for content when the sender gets the file back on the machine that holds the originals; it is lossy for character fields the target lacks.
- **The author's second machine** (a library restored from a normal backup keeps its sources at origin `local`) refuses a copy whose originals are not there with `package.own-source`, as it refuses a current share of the author's own content. Documented, not changed.
- Every release cut adds one ladder row and its bundled set; every new schema field needs a `Below` rule, which `SchemaCoverageTests` and `DownConvertCoverageTests` demand through the committed `requires-vN.json` list. The warn-or-block judgement for each new field is an owner decision.
- Characters that use bundled SRD revisions a target did not ship (the 0.8.0 packs) cannot be lowered for it; the message says to update the receiver.
- The lane C2 questions (spec §13, C2-Q1 to C2-Q6) were answered 2026-11-03:
  - Q1: the Q14 rule applies at every "newest" site (item 12).
  - Q2: `resource.counts` and `resourceFull` block; they cannot warn under v11.
  - Q3: a lowered draft restored next to its original draft is kept beside it, and the restore preview warns (`restore.down-converted-draft`); drafts are not re-pinned.
  - Q4: a sender who marks their own revisions as copies only demotes their own content.
  - Q5: `package.down-converted` is one line per file, with a count.
  - Q6: the copy check is not cached until measured.

## Alternatives rejected

- **Id from the original id and the target schema only** (no hash): one id would get two bodies when a converter rule or a referenced copy changes, which `package.revision-conflict` refuses on the receiver's side.
- **Keep the original revision id:** collides with the insert-only rule (`package.revision-conflict`).
- **Block a whole source pack or backup on a superseded revision's blocker:** one old revision would block a source with history forever.
- **A repair command on the newer build** that re-pins copies already stored to originals present: out of scope; restoring a lowered backup into an empty folder avoids the case.
- **Carry the dropped character fields as an inert key** for a lossless character round trip: rejected for scope.
- **Admit a copy under a local source when its marker is well-formed:** it would let a received file add revisions to the author's own source.
- **A package format bump for `madeFor`:** the target build would refuse the file made for it.
- **Offer 0.4.0:** needs a sub-floor character v7 writer for a release that opens no current character at all; the honest advice is "update to 0.5.0".

## Evidence

- `tests/RulesCore.Tests/DownConvertTests.cs`, `DownConvertV12Tests.cs`, `ItemModDownConvertTests.cs` (each lowering rule; for example `Character_below_9_blocks_a_running_encounter`, `Character_below_8_blocks_a_vulnerability_entry`) and `DownConvertCoverageTests.cs` (`Every_requires_vN_entry_has_a_below_rule`, `A_new_schema_version_needs_a_lowering_step_and_requires_vN_entries`).
- `tests/AppService.Tests/SchemaCoverageTests.cs` (`Every_schema_property_added_at_vN_is_listed`).
- `tests/AppService.Tests/ReleaseLadderTests.cs` (`CurrentRow_is_the_row_of_the_build_version`, `Constants_never_fall_below_the_current_row`, `Bundled_sets_are_subsets_of_the_catalog`, `OpensIn_skips_rows_lacking_a_bundled_revision`, `Target_accepts_offered_rows_only`).
- `tests/AppService.Tests/DownConversionExportTests.cs` (`Save_requires_matching_changesHash`, `Lower_rewrites_grants_and_choice_options_and_cascades`, `Uuid5_matches_known_vector`, `CanonicalBytes_and_CopyId_are_pinned`, `Import_repins_a_closure_all_or_nothing`, `Publish_strips_a_marker_a_draft_already_carries`, `Copy_never_counts_as_newest_while_an_unmarked_revision_exists`), and `NewestCopyTests.cs` (one `Copy_never_counts_as_newest_*` test per C2-Q1 site).
- `tests/AppService.Tests/DownConversionGoldenTests.cs` (`Golden_copy_matches_byte_for_byte`, `Cascade_rewritten_grant_points_at_the_subclass_copy_and_the_sheet_resolves_it`) over `tests/RulesFixtures/downconvert/for-0.8.0/`.
- `tests/AppService.Tests/MadeForMarkerTests.cs` (`Manifest_round_trips_madeFor`, `An_ordinary_export_does_not_write_madeFor`, `Manifest_ignores_malformed_madeFor`).
- `tests/AppService.Tests/LibraryBackupTests.cs` (`Backup_for_0_6_0_lowers_drafts_and_keeps_status_draft`, `Backup_leaves_out_a_superseded_blocked_revision_but_blocks_on_the_newest`, `Restore_repins_like_import`), `SourcePackTests.Source_pack_for_0_6_0_file_name_and_madeFor` and the `Campaign_pack_for_0_6_0_*` tests in `CampaignPackTests`.
- `tests/AppService.Tests/OldReleaseOutcomeTests.cs` (`Committed_outcome_matches_the_golden_inputs`) over `tests/RulesFixtures/downconvert/outcomes/`; the weekly job `old-release-check` in `.github/workflows/weekly.yml`.
- UI: `src/Ui/src/components/export/ChangeRegion.test.tsx`, `TargetFieldset.test.tsx`, `ExportConfirmation.test.tsx`, and the down-conversion cases in `CharacterSheet.test.tsx`, `SourcesPanel.test.tsx`, `CampaignsPanel.test.tsx` and `BackupsPanel.test.tsx`. No e2e flow covers it yet (the spec's e2e step is a follow-up).

Related: [ADR-007](ADR-007-export-package-and-license-policy.md) item 12, LIVING_SPECS D83 and D85, [package-format.md](../features/package-format.md#save-for-an-older-tomestack-d85). Supersedes: none.
