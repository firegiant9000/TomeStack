# Sheet layout: summary and tabs

SPEC P-03 · ADR-014 · LIVING_SPECS D17 (owner, 2026-10-03) · status: **slice 2 of 4** (the summary bar).

UI: `src/Ui/src/components/CharacterSheet.tsx`, `src/Ui/src/components/sheet/TabList.tsx`. Tests: `TabList.test.tsx` and every sheet test in `src/Ui/e2e/flow.e2e.tsx` (helper `openTab`).

## Layout

1. Header: name, rules family, level, campaign, "Level up", "Print…" (unchanged).
2. Print preview, when open (unchanged; `printable-backup.md`).
3. The summary (below).
4. Campaign warnings, "Choices to make" and "Content not applied": always visible, above the tabs.
5. The tab strip "Sheet sections" and one panel per tab. Inactive panels stay mounted and hidden, so nothing typed is lost.

## Summary (always visible)

A definition list, not headings or named regions, so it never shares a name with a panel. It shows:

- each ability's score with a "Roll <Ability> check (+N)" button (a d20 test in the chosen mode; the field cards keep "Roll <Ability> modifier" with the trace); an overridden ability modifier adds "(modifier overridden)" after its roll button;
- Proficiency bonus, Armor Class, Initiative; an overridden number reads "16 (overridden)";
- Hit points "current of maximum, N temporary", Hit dice per size, Inspiration or Heroic Inspiration (by family) as yes/no;
- Conditions and exhaustion, only when there are any;
- the d20 roll mode (Normal, Advantage, Disadvantage) and the "Last roll" live region, moved here from the old "Rolls" section so they are reachable from every tab.

Traces, overrides and "Report a gap" stay on the field cards.

## Tabs (slice 1)

| Tab | Holds |
| --- | --- |
| Sheet | Everything that was on the page except the admin panels and the notices above the tabs, in the old order |
| Manage | Updates available, Export, Export for a virtual tabletop, Archive, Snapshots |

## Keyboard

One tab stop on the strip. Left and Right move (and wrap), Home and End jump, and moving selects (automatic activation). Focus stays on the tab; the panel's controls follow in the tab order.

## No trade dress (P-03)

Only the information architecture (summary above, pages below) is borrowed. Labels are TomeStack's own section names; the strip is plain buttons in the system colours.

## Not yet

The split into Play, Spells, Inventory, Features, Stats and Notes (slice 3), the remembered tab and deep links (slice 4).
