# Sheet layout: summary and tabs

SPEC P-03 · ADR-014 · LIVING_SPECS D17 (owner, 2026-10-03) · status: **implemented** (slices 1 to 4, 2026-10-04) · ADR-015, D18 (2026-10-05).

UI: `src/Ui/src/App.tsx`, `src/Ui/src/components/CharacterSheet.tsx`, `src/Ui/src/components/sheet/TabList.tsx`, `src/Ui/src/components/SheetSummary.tsx`, `src/Ui/src/sheetTab.ts`. Tests: `TabList.test.tsx`, `SheetSummary.test.tsx`, `sheetTab.test.ts`, `CharacterSheet.test.tsx` and every sheet test in `src/Ui/e2e/flow.e2e.tsx` (helpers `openTab` and `summaryValue`).

## Layout

The shell is a grid with subgrid (ADR-015). Its rows, top to bottom:

1. The app header: the sidebar toggle ("Hide sidebar"/"Show sidebar") at the left, then "TomeStack" and the "Offline" tag. It carries the `--header-bg` tint of the theme's accent (30% in light, 12% in dark) and a 2px accent bottom edge (ADR-015 §1, amended 2026-10-06 and 2026-10-07).
2. The sheet header across the window: name, rules family, level, campaign, "Short rest…", "Long rest…", "Level up", "Print…" (rests moved here from Play, D29).
3. The summary across the window (below).
4. The rest proposal, when open (`rest` row, D29): across the window under the summary, a direct child of the article in DOM order too ([rests.md](rests.md)).
5. Messages (status and errors).
6. Print preview, when open, in the main column (`printable-backup.md`; a direct child of the article, after the summary in DOM order too, so focus order matches what is seen).
7. The sheet body in the main column: campaign warnings, "Choices to make" and "Content not applied" (always visible, above the tabs), then the tab strip "Sheet sections" (Play, Spells, Inventory, Features, Stats, Notes, Manage) and one panel per tab. Inactive panels stay mounted and hidden, so nothing typed is lost.

The main column starts on the Characters home screen: a card per active character (name, family, level, last change) with "Open {name}"; "Characters" in the sidebar's Tools returns to it. Archived characters stay in the sidebar's collapsed list (SPEC C-08). The home heading takes focus when the user navigates to it, but not at app start (the first Tab must reach the header's "Hide sidebar").

The sidebar sits on the left from the messages row down, so an open print preview or a long list of messages does not push it down.

Under 40rem everything flows in DOM order.

## Sidebar

The app header holds a button named "Hide sidebar" or "Show sidebar" (`aria-expanded`, `aria-controls`, `aria-keyshortcuts="Control+B"`). Inside the sidebar, first a "Close sidebar" button (the same toggle; focus lands on the header's "Show sidebar"), then the heading "Characters" with the list, then "Archived (N)", then the heading "Tools" with the eleven action buttons ("Characters" first) (owner, 2026-10-06: the list comes first because it is what the sidebar is for). The nav takes its name from the "Characters" heading. Ctrl+B does the same (key auto-repeat is ignored). When the hidden sidebar held focus, focus moves to the toggle. The choice is remembered in the page's storage (`tomestack.sidebar`); collapsed, the main column keeps its left inset. The sidebar has `z-index: 1`, because the main column and the sheet article span its column and would otherwise take its clicks; the messages have it too, because the article spans their cell.

## Summary (always visible)

A definition list, not headings or named regions, so it never shares a name with a panel. Since 2026-10-06 every item is a box (a 1px `--line-strong` border on the existing `dl > div`, no shadow; the generic paper-sheet convention, SPEC P-03). An ability box shows the modifier large with the score beside it on the baseline (or the reverse, Settings "Ability boxes"); the d20 roll mode and the Last roll share one full-width strip under the boxes with a reserved height, so an ordinary roll does not move the tabs (a Spend button or a variable-cost form can still grow the strip). Under the Last roll, **Previous rolls**: a collapsed list of the last ten rolls of this sheet session (newest first, the current roll excluded, hit dice from a rest included, and a hit die stays there even if it is removed from the rest or the rest is cancelled, because it was rolled), with the time; not announced, not stored, cleared when the sheet closes (D28). It shows:

- each ability's modifier large, its score beside it and a compact **Roll** button whose accessible name is "Roll <Ability> check (+N)" (D28; outlined in the accent colour with a small hexagon glyph; a d20 test in the chosen mode; the field cards keep "Roll <Ability> modifier" with the trace); an overridden ability modifier adds "(modifier overridden)" after its roll button;
- Proficiency bonus, Armor Class, Initiative, Speed (30 ft. by rules policy until species content carries it; override on Stats); an overridden number reads "16 (overridden)";
- Hit points "current of maximum, N temporary" (an overridden maximum reads "(maximum overridden)"), Hit dice per size as "N of M (dX)", Inspiration or Heroic Inspiration (by family) as a checkbox (the confirmed `setInspiration` play action), and one-point hit-point buttons "Lose 1 hit point" and "Regain 1 hit point" (`damage`/`heal`) in a second `<dd>`;
- Conditions and exhaustion, only when there are any;
- the d20 roll mode (Normal, Advantage, Disadvantage) as one row of radios with 3:1 boundaries (`--line-strong`) and the "Last roll" live region, moved here from the old "Rolls" section so they are reachable from every tab.

Traces, overrides and "Report a gap" stay on the field cards.

## Tabs

| Tab | Holds |
| --- | --- |
| Play (default) | Hit points (damage, healing, temporary hit points, hit dice, inspiration), death saving throws, Conditions, Resources, Class columns, Attacks and actions; at 60rem and up, Attacks and actions sit in a right-hand column (in one column they follow Resources and Class columns); conditions are outlined chips; a "Compact view" checkbox (remembered, `tomestack.compactPlay`) hides only the explanatory parts (resource and attack traces, the hit point and Concentration explanations, class columns) for combat; spend and recovery text stay (rests moved to the sheet header, D29) (D20) |
| Spells | Spells and slots, and the Spellcasting field cards (spell attack, save DC, slots) with their traces and overrides. Offered only to a caster, or when a spell field has a value or override; slot pips beside "N of M" |
| Inventory | Equipment, listed as Equipped and Carried; Currency (cp, sp, ep, gp, pp; D21) |
| Features | Features, with "Open page" and "Report a gap" ("No features yet." when there are none), grouped by what granted them ("From Fixture Fighter") or by kind (Classes, Subclasses, Species, Background, Feats, Granted features, Spells, Items) |
| Stats | The field cards: Abilities, Proficiency, Saving throws, Skills, Combat, each with its trace, roll button, override form and "Report a gap"; two columns at 60rem and up. Saving throws and Skills show " · proficient" or " · expertise" after the value when a grant applies; Skills also list Passive Perception, Insight and Investigation (10 + the skill; calculated, overridable on Stats; content cannot target them until a content schema version allows it); Combat lists Speed |
| Notes | Session notes (dated, newest first; D22) above Gap notes |
| Manage | Updates available, Export, Export for a virtual tabletop, Archive, Snapshots, separated by rules |

"Report a gap" on a feature or a field opens Notes, pre-fills "About" and moves focus to the note text (WCAG 2.4.3). Open choices and content problems stay above the tabs on every page (SPEC C-03).

## Keyboard

One tab stop on the strip. Left and Right move (and wrap), Home and End jump, and moving selects (automatic activation). Focus stays on the tab; the panel's controls follow in the tab order.

Ctrl+plus, Ctrl+minus and Ctrl+0 zoom the window (the host handles them; WebView2's other browser shortcuts are off, ADR-006). Settings lists every shortcut.

## No trade dress (P-03)

Only the information architecture is borrowed. The palette is TomeStack's own (ADR-015): three themes on the system colours, no red or parchment. Dice are plain polygons. The labels are plain words; some also appear in other digital sheets, which the owner accepted on 2026-10-04. The tab glyphs are plain geometric shapes drawn in CSS (D26): no icon set, nothing from another product.

## Settings

A Settings screen in four groups. **Appearance:** Colour scheme (System, Light, Dark; `tomestack.appearance`, applied as `html[data-appearance]` so `light-dark()` resolves without the OS; WebView2's own print dialog keeps following Windows), Theme (forest, cool, violet), Text size (90% to 200%; `tomestack.textSize`, the root font size; browser zoom multiplies with it), Ability boxes (Modifier first or Score first; `tomestack.abilityOrder`) and "Animate dice". **Accessibility:** Motion (Follow Windows or Reduce motion; `tomestack.motion`), Stronger borders and labels (`tomestack.contrast`: `--line` → 50%, `--line-strong` → 70%, `--muted` → 80% of CanvasText), Always show the focus outline (`tomestack.focus`), Larger buttons (`tomestack.targets`, 44px), Underline links and text buttons (`tomestack.underline`), Announce each roll to screen readers (`tomestack.announceRolls`; off removes `aria-live` from the Last roll region, the text stays). Windows contrast themes (forced colours) override all of these. **Keyboard shortcuts:** a static list. **About:** the version and data schema. Every choice is kept in the page's storage: on this computer only, in no backup, package or share (ADR-015 §4).

## Remembered tab and deep links

The sheet reopens on the tab used last for that character. The memory is `tomestack.sheetTab.<characterId>` in the page's own storage (the WebView2 profile in the data folder): kept in this data folder only, in no package, share or backup. "Open <character>" on the Gap notes screen opens Notes instead (and leaves the memory alone). A tab that is not offered (Spells for a character who is no longer a caster) falls back to Play, and the memory is left alone: it changes only when you pick a tab, so a remembered Spells returns if the character becomes a caster again. If the open tab stops being offered while you are on it (the last spell override is removed on a non-caster), the sheet moves to Play and, only if focus would otherwise be lost, puts focus on the selected tab (Play, since that is where it falls back to).

## Reflow

Everything is in rem with no fixed widths. Under 40rem the sidebar stacks above the content; the abilities grid (`auto-fit, minmax(7rem, 1fr)`), the sheet header and the tab strip wrap. The owner's 200% and 400% check in WebView2 is pending (accessibility checklist item 6). The main column and the sheet's own grid are `minmax(0, 1fr)`, so a wide table or excerpt can no longer widen the window: the app header and the sheet header's buttons stay in view, and only the wide element itself overflows; the app header wraps. The sheet bar spans the window (ADR-015), so the 200% and 400% check also covers it with the sidebar collapsed and expanded. Text size above 125% moves the one-column breakpoint to 60rem, because a rem media query uses the browser's 16px, not the root override.
