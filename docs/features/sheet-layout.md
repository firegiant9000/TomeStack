# Sheet layout: header, status bar, tabs and More

SPEC P-03 · ADR-014 · LIVING_SPECS D17 (owner, 2026-10-03) · status: **implemented** (slices 1 to 4, 2026-10-04) · ADR-015, D18 (2026-10-05) · D96 (status bar, six tabs and More, Wave 3 Groups 3 and 4).

UI: `src/Ui/src/App.tsx`, `src/Ui/src/components/CharacterSheet.tsx`, `src/Ui/src/components/sheet/TabList.tsx`, `src/Ui/src/components/sheet/MoreMenu.tsx`, `src/Ui/src/components/SheetSummary.tsx`, `src/Ui/src/sheetTab.ts`. Tests: `TabList.test.tsx`, `MoreMenu.test.tsx`, `SheetSummary.test.tsx`, `sheetTab.test.ts`, `CharacterSheet.test.tsx` and every sheet test in `src/Ui/e2e/*.e2e.tsx` (helpers `openTab` and `summaryValue` in `src/Ui/e2e/helpers.tsx`).

## Layout

The shell is a grid with subgrid (ADR-015). Its rows, top to bottom:

1. The app header: the sidebar toggle ("Hide sidebar"/"Show sidebar") at the left, then "TomeStack" and the "Offline" tag. It carries the `--header-bg` tint of the theme's accent (30% in light, 12% in dark) and a 2px accent bottom edge (ADR-015 §1, amended 2026-10-06 and 2026-10-07).
2. The sheet header across the window: name, rules family, level, campaign, "Short rest…", "Long rest…", "Level up" (rests moved here from Play, D29) and "More" ([More](#more)).
3. The status bar across the window ([below](#status-bar-always-visible)).
4. The rest proposal, when open (`rest` row, D29): across the window under the summary, a direct child of the article in DOM order too ([rests.md](rests.md)).
5. Messages (status and errors).
6. The panel a More item opened, when one is open: the print preview or the More panels region ([More](#more); a direct child of the article, after the rest panel, the after-rest panel and the damage panel in DOM order, so focus order matches what is seen). A rest panel and a More panel can be open at the same time; choosing a More item while one is open replaces it (Print… included: choosing it again re-opens it).
7. The sheet body in the main column: campaign warnings, "Choices to make" (with its "Choose now" button) and "Needs your attention" (always visible, above the tabs), then the tab strip "Sheet sections" (Play, Spells, Gear, Features, Skills and saves, Journal) and one panel per tab. Inactive panels stay mounted and hidden, so nothing typed is lost.

"Choices to make" lists each open choice once, with "Choose now" opening Make choices. "Needs your attention" lists content that was not applied. An open choice's own diagnostic (`choice.unresolved`) is matched to it by the choice's effect id and left out of "Needs your attention", so a choice is never listed twice. The sheet shows the service's messages as they come; it does not rewrite them.

The main column starts on the Characters home screen: a card per active character (name, family, level, last change) with "Open {name}"; "Characters" in the sidebar's Tools returns to it. Archived characters stay in the sidebar's collapsed list (SPEC C-08). The home heading takes focus when the user navigates to it, but not at app start (the first Tab must reach the header's "Hide sidebar").

The sidebar sits on the left from the messages row down, so an open print preview or a long list of messages does not push it down.

Under 40rem everything flows in DOM order.

## Sidebar

The app header holds a button named "Hide sidebar" or "Show sidebar" (`aria-expanded`, `aria-controls`, `aria-keyshortcuts="Control+B"`). The sidebar has three labelled groups, Characters, Library and App (Backups, Help, Settings; D94, Wave 3 Groups 2 and 11), and the nav is named "Main". Ctrl+B does the same (key auto-repeat is ignored). When the hidden sidebar held focus, focus moves to the toggle. The choice is remembered in the page's storage (`tomestack.sidebar`); collapsed, the main column keeps its left inset. The sidebar has `z-index: 1`, because the main column and the sheet article span its column and would otherwise take its clicks; the messages have it too, because the article spans their cell.

## More

"More" is the last button in the sheet header. It is a disclosure, not an ARIA menu: a button with `aria-expanded` and `aria-controls` that shows a plain list of buttons, so Tab walks the items. Esc closes the list and returns focus to "More". The list also closes when focus leaves it (Tab past the last item, or a click elsewhere). The items, in order:

- **Choices**, only when the sheet has choices (it opens Make choices; it was "Change picks", MECH-X item 2);
- **Print…**, which opens the print preview ([printable-backup.md](printable-backup.md)); the header has no Print button;
- **Export…**, the TomeStack export and the Foundry (virtual tabletop) export together;
- **Snapshots**, **Updates** and **Archive**.

Choosing an item opens its panel in one region under the header (choosing another replaces it, and choosing the same one again moves focus to its heading again). Focus moves to the panel's heading, and its "Close" button returns focus to "More". The print preview takes focus to its own "Print character" heading and closes with "Close print preview". Updates reads "Checking for newer versions…" while it looks, then offers updates or says "No newer versions of the content this character uses."

## Status bar (always visible)

**Wave 3 (D96): the summary is the Status bar.** One region named "Status", sticky at the top while you scroll when it is at most a third of the window tall (static otherwise; `--status-bar-height` and `--status-bar-pin` are set from its measured height). It holds Hit points with the one-point buttons, Armor Class, Initiative and Speed (numbers that open their trace), Proficiency bonus, the Inspiration checkbox, the conditions text, the roll mode, the dice box and the Last roll with "Earlier rolls". The six ability boxes are an "Abilities" panel on Play unless Settings "Ability boxes at the top of the sheet" (`tomestack.abilityBoxesTop`) puts them above the bar.

It shows, in order:

- **Hit points** as "current of maximum, N temporary" (a maximum set by hand reads "(maximum set by hand)"), with the one-point buttons "Lose 1 hit point" and "Regain 1 hit point" and a "Bloodied" chip when the character is bloodied;
- **Armor Class**, **Initiative** and **Speed** (Speed adds Fly, Climb and Swim when they are above 0 or set by hand), each a button that opens its "why" trace in one region (`#status-why`); a value set by hand reads "(set by hand)";
- **Proficiency bonus**;
- the **Inspiration** checkbox (Heroic Inspiration by family; the confirmed `setInspiration` play action);
- **Conditions** and exhaustion, only when there are any, and **Attacks against you**, when the sheet has any;
- the **quickbar** (D73), in its own row, only when it has buttons;
- the d20 **roll mode** (Normal, Advantage, Disadvantage) and the **dice box**;
- the **Last roll** region (`role="status"`, the one live node) with **Earlier rolls** under it (called "Previous rolls" before Wave 3): a collapsed list of the last ten rolls of this sheet session (newest first, the current roll excluded, hit dice from a rest included, and a hit die stays there even if it is removed from the rest or the rest is cancelled, because it was rolled), with the time; not announced, not stored, cleared when the sheet closes (D28).

The ability boxes are not in the bar: they are the "Abilities" panel on Play, or sit above the bar with the setting. Hit dice are not in the bar either; they are in the Hit points panel on Play. The Last roll shares a strip with a reserved height, so an ordinary roll does not move the tabs (a Spend button or a variable-cost form can still grow the strip).

The **dice box** (tester feedback item 8; a form named "Roll dice" with a "Dice to roll" text box) sits under the d20 mode row in the same grid cell, outside the fixed roll strip, so the strip's `min-height` is unchanged. It wraps onto further lines in a narrow column (headless Edge, measured again on 2026-10-15 against the current markup, with the box's row wrapper, the always-mounted empty message, the result's dice, text and detail lines, and the closed "Earlier rolls" line: at 1280 px the strip is 152 px with a short result and 169 px with a long one; at a true 320 px viewport it is 152 px and 238 px; each is the same with or without the box, and an open refusal under the box, which grows the box from 64 px to 109 px at 320 px, leaves the short strip at 152 px). See [dice-engine.md](dice-engine.md) "Rolling from the summary".

Traces, overrides and "Report a gap" stay on the field cards.

## Tabs

| Tab | Holds |
| --- | --- |
| Play (default) | Hit points (damage, healing, temporary hit points, hit dice, inspiration), death saving throws, Conditions, Resources, Class columns, Attacks and actions; at 60rem and up, Attacks and actions sit in a right-hand column (in one column they follow Resources and Class columns); conditions are outlined chips; a "Compact view" checkbox (remembered, `tomestack.compactPlay`) hides only the explanatory parts (resource and attack traces, the hit point and Concentration explanations, class columns) for combat; spend and recovery text stay (rests moved to the sheet header, D29) (D20) |
| Spells | Spells and slots, and the Spellcasting field cards (spell attack, save DC, slots) with their traces and overrides. Offered only to a caster, or when a spell field has a value or override; slot pips beside "N of M"; a search by name per caster (D30) |
| Gear | Equipment, listed as Equipped and Carried; your own items and attacks; Currency (cp, sp, ep, gp, pp; D21) |
| Features | Features, with "Open page" and "Report a gap" ("No features yet." when there are none), grouped by what granted them ("From Fixture Fighter") or by kind (Classes, Subclasses, Species, Background, Feats, Granted features, Spells, Items). After a level-up it starts with a dismissible "What changed at level N" panel (D74): new features, choices, hit points, slots, pools, forms and the features that are by hand from now on, with links to Play, Spells and "Choices to make". It stays on the Features tab until dismissed. The tab ends with the features that are by hand, then **Gap notes** (the form and the list; [gap-notes.md](gap-notes.md)) |
| Skills and saves | The field cards: Abilities, Proficiency, Saving throws, Skills, Combat, each with its trace, roll button, override form and "Report a gap"; two columns at 60rem and up. Saving throws and Skills show " · proficient" or " · expertise" after the value when a grant applies; Skills also list Passive Perception, Insight and Investigation (10 + the skill; calculated, overridable on Stats; content cannot target them until a content schema version allows it); Combat lists Speed; below the cards, "Languages and tools" lists the character's languages and tools read-only (nothing shows while both are empty) with an "Edit in Journal → Details" button that opens Journal and focuses the Details heading (item 17) |
| Journal | Session notes (dated, newest first; D22), then Details (item 17, D36): a form named "Details" of four fieldsets (Personality, Appearance, Background story, Languages and tools) |

There is no Manage tab: Export, Print, Snapshots, Updates and Archive are in [More](#more).

After a long rest that finishes with a `longRest` replaceable choice that has picks, the sheet shows an "After your long rest" panel above the tabs, with "Make choices" and "Dismiss" ([rests.md](rests.md), MECH-X item 2). It is page state: not stored, and gone on reload. At any other time, "Choices" in More (listed whenever the character has a choice) opens Make choices, even when every choice is answered.

"Report a gap" on a feature or a field opens Features, pre-fills "About" and moves focus to the note text (WCAG 2.4.3). Open choices and content problems stay above the tabs on every page (SPEC C-03).

## Keyboard

One tab stop on the strip. Left and Right move (and wrap), Home and End jump, and moving selects (automatic activation). Focus stays on the tab; the panel's controls follow in the tab order.

Ctrl+plus, Ctrl+minus and Ctrl+0 zoom the window (the host handles them; WebView2's other browser shortcuts are off, ADR-006). Help lists every shortcut (D94, Wave 3 Group 11).

## No trade dress (P-03)

Only the information architecture is borrowed. The palette is TomeStack's own (ADR-015): three themes on the system colours, no red or parchment. Dice are plain polygons. The labels are plain words; some also appear in other digital sheets, which the owner accepted on 2026-10-04. The tab glyphs are plain geometric shapes drawn in CSS (D26): no icon set, nothing from another product.

## Settings

A Settings screen in four groups (the keyboard shortcuts moved to Help in Wave 3 Group 11, D94). **Appearance:** Colour scheme (System, Light, Dark; `tomestack.appearance`, applied as `html[data-appearance]` so `light-dark()` resolves without the OS; WebView2's own print dialog keeps following Windows), Theme (forest, cool, violet), Text size (90% to 200%; `tomestack.textSize`, the root font size; browser zoom multiplies with it), Ability boxes (Modifier first or Score first; `tomestack.abilityOrder`) and "Animate dice". **Accessibility:** Motion (Follow Windows or Reduce motion; `tomestack.motion`), Stronger borders and labels (`tomestack.contrast`: `--line` → 50%, `--line-strong` → 70%, `--muted` → 80% of CanvasText), Always show the focus outline (`tomestack.focus`), Larger buttons (`tomestack.targets`, 44px), Underline links and text buttons (`tomestack.underline`), Announce each roll to screen readers (`tomestack.announceRolls`; off removes `aria-live` from the Last roll region, the text stays). Windows contrast themes (forced colours) override all of these. **About:** the version and data schema. Every choice is kept in the page's storage: on this computer only, in no backup, package or share (ADR-015 §4).

**Diagnostics:** "Copy diagnostics" (`diagnostics.copy`, read-only) shows text for a bug report in a read-only box, already selected, with a "Select all" button; the user presses Ctrl+C, because the app has no clipboard route and sends nothing anywhere. The service builds it (`src/AppService/Diagnostics/DiagnosticsCopy.cs`): the app version, the Windows version, the data folder's **name** only and the newest 20 `errors.log` entries (the rolled-over file included). Of each entry it copies only the time, the correlation id, the command, the exception type names (with the code a COM, SQLite or Win32 error prints), the stack frames as the method name (the parameter list shows as `(...)`) and the source **file name** only, and the shell's fixed WebView2 line. Exception messages are left out, because a message can quote a title or a file name, and no folder path is copied at all (a build or OneDrive path can name a person or an employer). A line shaped like a frame that holds anything but code names is dropped or shows `(file)`. In the names that are copied (the data folder, source files, the command), the Windows user name becomes `<user>` and every source title in the library `source:<id>`. Redaction runs on the raw text, not an encoded copy. Gap-note and homebrew text never reach it: the log holds no payloads, and the message filter guards the reader too. The log reader starts an entry only at a header after the writer's empty line, so a header-shaped line inside a message stays in its own entry; if the log exists but cannot be read, the text says so instead of "No errors have been logged."

## Remembered tab and deep links

The sheet reopens on the tab used last for that character. The memory is `tomestack.sheetTab.<characterId>` in the page's own storage (the WebView2 profile in the data folder): kept in this data folder only, in no package, share or backup. "Open <character>" in Help's "Gap notes" section opens Features instead (and leaves the memory alone). A remembered id that is no longer a tab (the old "manage") opens Play. A tab that is not offered (Spells for a character who is no longer a caster) falls back to Play, and the memory is left alone: it changes only when you pick a tab, so a remembered Spells returns if the character becomes a caster again. If the open tab stops being offered while you are on it (the last spell override is removed on a non-caster), the sheet moves to Play and, only if focus would otherwise be lost, puts focus on the selected tab (Play, since that is where it falls back to).

## Reflow

Everything is in rem with no fixed widths. Under 40rem the sidebar stacks above the content; the abilities grid (`auto-fit, minmax(7rem, 1fr)`), the sheet header and the tab strip wrap. The owner's 200% and 400% check in WebView2 is pending (accessibility checklist item 6). The main column and the sheet's own grid are `minmax(0, 1fr)`, so a wide table or excerpt can no longer widen the window: the app header and the sheet header's buttons stay in view, and only the wide element itself overflows; the app header wraps. The sheet bar spans the window (ADR-015), so the 200% and 400% check also covers it with the sidebar collapsed and expanded. Text size above 125% moves the one-column breakpoint to 60rem, because a rem media query uses the browser's 16px, not the root override.
