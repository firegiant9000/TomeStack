# M3 acceptance: Personal replacement, exit evidence

ROADMAP M3 exit gate: "Arlo plays that character end to end without D&D Beyond" · MVP "Separate replacement milestone (M3)" · build **0.3.0** · status: **not met.** The engineering items are done and tested. The gate needs the owner's private material and a played session, and neither was available to the development session (2026-09-28). Following ADR-008, the version stays 0.3.0 until the gate is met (0.4.0 then). **Evidence level:** every M3 item is at most fixture-verified; none is accepted in real play, which is what the gate needs. The run now waits on ROADMAP M2.2 (the Fighter baseline) as well as the material.

## MVP M3 checklist ("Test complex choices, multiclass, spellcasting if applicable, item interactions, short/long rest recovery, update review and printable backup")

| Item | Executable acceptance | Status |
| --- | --- | --- |
| The actual Stardust Guardian character imports with every mechanic classified (B1) | `StardustGuardianAcceptanceTests` (skips without `tests/RulesFixtures/local/stardust-guardian/character.tomestack.zip`); its synthetic stand-in runs in the gate | **Not run: the material is absent.** Synthetic ✅ |
| Complex resource and action mechanics (B2) | `ToggleAndCostTests`, `ToggleCommandTests`, e2e "switches a toggled effect on and off…" | ✅ |
| Session gap notes (B3), "Report a gap" and the notes of all characters (C5) | `GapNoteTests`, e2e "records a gap note on a field and a feature…" | ✅ |
| Multiclass spellcasting: combined slots, Pact Magic separate (C3, D04's M3 part) | `MulticlassSpellSlotTests`, `SrdCasterTests.A_Sorcerer_Paladin_combines_slots_on_the_multiclass_table_differently_per_family_side_by_side`, `SrdCasterTests.Pact_Magic_stays_separate_from_a_single_casters_own_table` | ✅ |
| Printable backup (C4) | e2e "prints a sheet with its license notices, and gap notes only when ticked" | ✅ automated · owner ☐: the shell's print dialog and the printed page |
| Update review and source updates (C7) | `SourceUpdateTests`, `PublishingTests`, e2e "authors a homebrew subclass… reviews an update" | ✅ |
| Complex choices, item interactions, short and long rests | M2 evidence (`m2-acceptance.md`): `ChoiceTests`, `EquipmentTests`, `RestCommandTests`, `ShortRestAndHitDiceTests`, `M2AcceptanceTests`, the e2e flows | ✅ on SRD and fixture content; ☐ on the real character |
| A whole session played with the character, every gap note fixed or accepted | Owner | ☐ **not played** |

## B1 result (counts only)

**Not run.** `tests/RulesFixtures/local/` does not exist on the development machine, so there is no report, and there are no counts of mechanics, unsupported effect types or misclassifications to give. The test skipped as designed (`dotnet test`: 1 skipped).

## Items that depend on the B1 run

- **C1 (report the gaps) and C2 (effects for the approved gaps): skipped**, because they need the B1 report. The C2 candidates stay designed but unbuilt until the report asks for them, as `m3-effects.md` "Not yet" lists: timed toggles, mutually exclusive toggles, toggled grants and proficiencies, and costs paid in hit points or spell slots.
- **C6 (more SRD content): deferred by owner decision (2026-09-28).** Without the material there is no measured need. Barbarian levels 4–20, Fighter, Monk, Rogue, SRD armor, and more species and backgrounds stay unbundled (`m2-acceptance.md`, "Known limits"). Revisit after the B1 run. **Partly reversed later on 2026-09-28 (owner direction):** the Fighter and the SRD armor table move *before* the B1 run as ROADMAP M2.2, because the Stardust Guardian is built on a Fighter and the run cannot happen without one. Monk, Rogue, Barbarian 4–20 and more species and backgrounds stay deferred.

## The played session

Not played. The procedure, for when the material is in place:

1. Put the personal backup in `tests/RulesFixtures/local/stardust-guardian/` (see `m3-stardust-guardian.md`). Run `dotnet test --filter StardustGuardianAcceptanceTests`, and read `report.md` there.
2. Automate what the report shows missing (C2), or accept it as assisted or reference.
3. Restore the same backup into the installed app, play a whole session, and record every shortfall with "Report a gap".
4. Work through **Gap notes** in the sidebar: fix each note or accept it, then mark it resolved.
5. Record here, as counts only: the notes written, fixed and accepted, and that no D&D Beyond was used.

| Date | Build | Notes written | Fixed | Accepted | Result |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

## Could not be verified here

- The owner's Stardust Guardian material, the B1 run, and the played session (above).
- The WebView2 print dialog and the printed page in the installed app (`printable-backup.md`). jsdom proves the preview and the `window.print()` call only.
- The C7 offers on a real upgrade: an installed 0.3.0 data folder opened by the next build. `SourceUpdateTests` simulates it by adding the newer revision to the store.
