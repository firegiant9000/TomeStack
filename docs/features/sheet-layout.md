# Sheet layout: summary and tabs

SPEC P-03 · ADR-014 · LIVING_SPECS D17 (owner, 2026-10-03) · status: **slice 3 of 4** (the summary bar and the seven tabs).

UI: `src/Ui/src/components/CharacterSheet.tsx`, `src/Ui/src/components/sheet/TabList.tsx`, `src/Ui/src/components/SheetSummary.tsx`, `src/Ui/src/sheetTab.ts`. Tests: `TabList.test.tsx`, `SheetSummary.test.tsx`, `CharacterSheet.test.tsx` and every sheet test in `src/Ui/e2e/flow.e2e.tsx` (helpers `openTab` and `summaryValue`).

## Layout

1. Header: name, rules family, level, campaign, "Level up", "Print…" (unchanged).
2. Print preview, when open (unchanged; `printable-backup.md`).
3. The summary (below).
4. Campaign warnings, "Choices to make" and "Content not applied": always visible, above the tabs.
5. The tab strip "Sheet sections" (Play, Spells, Inventory, Features, Stats, Notes, Manage) and one panel per tab. Inactive panels stay mounted and hidden, so nothing typed is lost.

## Summary (always visible)

A definition list, not headings or named regions, so it never shares a name with a panel. It shows:

- each ability's score with a "Roll <Ability> check (+N)" button (a d20 test in the chosen mode; the field cards keep "Roll <Ability> modifier" with the trace); an overridden ability modifier adds "(modifier overridden)" after its roll button;
- Proficiency bonus, Armor Class, Initiative; an overridden number reads "16 (overridden)";
- Hit points "current of maximum, N temporary" (an overridden maximum reads "(maximum overridden)"), Hit dice per size, Inspiration or Heroic Inspiration (by family) as yes/no;
- Conditions and exhaustion, only when there are any;
- the d20 roll mode (Normal, Advantage, Disadvantage) and the "Last roll" live region, moved here from the old "Rolls" section so they are reachable from every tab.

Traces, overrides and "Report a gap" stay on the field cards.

## Tabs

| Tab | Holds |
| --- | --- |
| Play (default) | Hit points (damage, healing, temporary hit points, hit dice, inspiration), death saving throws, short and long rest, Attacks and actions, Conditions, Resources, Class columns |
| Spells | Spells and slots, and the Spellcasting field cards (spell attack, save DC, slots) with their traces and overrides. Offered only to a caster, or when a spell field has a value or override |
| Inventory | Equipment |
| Features | Features, with "Open page" and "Report a gap" |
| Stats | The field cards: Abilities, Proficiency, Saving throws, Skills, Combat, each with its trace, roll button, override form and "Report a gap" |
| Notes | Gap notes |
| Manage | Updates available, Export, Export for a virtual tabletop, Archive, Snapshots |

"Report a gap" on a feature or a field opens Notes, pre-fills "About" and moves focus to the note text (WCAG 2.4.3). Open choices and content problems stay above the tabs on every page (SPEC C-03).

## Keyboard

One tab stop on the strip. Left and Right move (and wrap), Home and End jump, and moving selects (automatic activation). Focus stays on the tab; the panel's controls follow in the tab order.

## No trade dress (P-03)

Only the information architecture (summary above, pages below) is borrowed. Labels are TomeStack's own section names; the strip is plain buttons in the system colours.

## Not yet

Slice 4: the remembered tab (per character, in the page's own storage), the deep link from the Gap notes screen, and the reflow of the summary at narrow widths.
