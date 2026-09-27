# Restrictions and content validation

SPEC C-01, C-03, I-02, I-06, Q-02 · ADR-003, ADR-004 · status: implemented (M1 item 3).

## Restrictions are prerequisites

A `restriction` effect (`field`, `minimum`), such as "Strength 13 or higher", is checked before its content applies (`CharacterCalculator.Calculate`, `tests/RulesCore.Tests/RestrictionTests.cs`):

1. The sheet is calculated once with everything.
2. Each active revision with an automatic restriction is checked against the sheet calculated **without that revision**. So content cannot qualify itself: a feat that raises Strength does not meet its own Strength prerequisite.
3. Content that fails is left out of the final calculation, together with anything it grants or offers. A `restriction.unmet` diagnostic, scoped to the content and the restriction effect, names the requirement and the value the character has.
4. Checks use the displayed values, so a user override counts (SPEC C-06), and the trace shows the override.
5. A restriction on a field that does not exist keeps its content out, with `effect.unknown-target`.

Each restricted revision is checked with the other restricted revisions present. That is exact for independent prerequisites, which is every SRD case in the M1 slice.

## Content validation before publish

`ContentValidator.Validate(revision, catalog, batch?)` in the rules core, and the command `content.validate { reference }` (a stored revision) or `{ revision }` (an unsaved one). It returns `{ errors, warnings, canPublish }` and writes nothing. Publishing refuses a revision with errors (M1 item 2).

| Area | Errors (block publishing) | Warnings |
| --- | --- | --- |
| Schema | Missing ids or name; no, unknown or duplicate rules families; invalid page range; duplicate or empty effect ids, choice ids or resource ids; unknown targets; `highestInGroup` without a group; levels outside 1–20; a choice with no options, duplicate options or a count above the options; hit die not d6–d12, or more than one; v3 features (levels, `hitDie`, `armorClass` / `hitPoints`) in a revision that declares v2 | An effect type this build does not automate; a hit die on non-class content; a class without a hit die; a recovery for a resource this revision does not define |
| References | Source not installed; granted content or a choice option missing, or supporting none of this revision's families; content naming itself | A referenced revision that is still a draft |
| Formulas | Modifier values, resource maximums, recovery amounts (unless `all`) that do not parse; roll dice that do not parse | |
| Cycles | Modifiers that would create a dependency cycle with the base field graph (`effect.dependency-cycle`) | |

Revisions validated together (`batch`) may reference each other. Every fixture revision validates without errors (`ContentValidatorTests`).
