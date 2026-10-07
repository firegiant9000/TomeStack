# ADR-015: Visual language and shell layout: tokens, three themes, a collapsible sidebar, a full-width sheet bar, a dice display

Status: accepted (owner, 2026-10-05; LIVING_SPECS D18)
Date: 2026-10-05
Implementation: PR `ui-refresh` (built on PR `ui-quick-fixes`), features/sheet-layout.md, features/sheet-play.md, features/dice-engine.md.

## Context

D17 and ADR-014 built the summary and tabs in "plain system styling, no new colours" as a provisional look. After importing a real character the owner asked for a collapsible sidebar, the sheet header and summary across the whole window, a refreshed look with more colour, animated dice, and Inspiration and quick hit-point changes in the summary. SPEC P-03 forbids D&D Beyond trade dress; D05 requires WCAG 2.2 AA; ADR-006 means everything is bundled, and the build's CSP is `style-src 'self'` (no inline styles). The e2e flow scopes every sheet query to the character's `article`, and print depends on the print view being a direct child of that article. An investigation before implementation (2026-10-05) computed every contrast ratio and hit-tested the layout in headless Edge.

## Decision

1. **Tokens** in `styles.css`: `--surface`, `--accent`, `--accent-2`, `--warn`, `--error` on the system `Canvas`/`CanvasText` base, `--line` (decoration) and `--line-strong` (control boundaries, 3:1), a five-step spacing scale, a five-step type scale, one radius, and `--font-numbers` (Bahnschrift, a Windows system font, falling back to `system-ui`; nothing bundled). **Three themes**: forest (the default, in `:root`), cool and violet (`html[data-theme]`), each light and dark via `light-dark()`. No red, no parchment. Text on a filled accent shape is always `--bg`. The surface fill goes on the summary bar and the sidebar only; panels are headings and rules, not cards. A `forced-colors` block maps every token to a system colour; a `prefers-reduced-motion` block removes all motion. Dark values lifted on 2026-10-06 (owner): forest #8fe0a8 / #ebd070 / #1b241c, cool #aeb8ff / #73ebdb / #1e2230, violet #d6beff / #86e3f5 / #211a2c; light values unchanged (they sit near the 6:1 floor).
2. **Shell grid with subgrid**: `.app` names columns `side main` and rows `top head summary messages print body`; `main` and the sheet article are subgrids, so the sheet's header and summary take the `head` and `summary` rows across the window while staying direct children of the article (print and e2e unchanged). The rest of the sheet is one `div.sheet-body` in the `main` column. The sidebar has `z-index: 1`, because `main` and the article span its column and would otherwise take its clicks. Under 40rem everything flows in DOM order. Focus order stays header → sidebar → sheet, the conventional navigation-first order, although the bar sits above the sidebar visually.
3. **Collapsible sidebar**: a "Hide sidebar"/"Show sidebar" button in the app header (`aria-expanded`, `aria-controls`, `aria-keyshortcuts="Control+B"`), Ctrl+B, focus moved to the toggle when the hidden sidebar held it, remembered in page storage (`tomestack.sidebar`). The header shows an "Offline" tag; the version and schema moved to Settings.
4. **Settings screen** (theme, dice animation, version) in page storage (`tomestack.theme`, `tomestack.diceAnimation`): in no backup, package or share, like the sheet tab memory.
5. **Dice display**: in the "Last roll" live region, `aria-hidden` spans drawn with `clip-path` (a round fallback for sizes other than d4 to d20 and d100), at most ten drawn, their face by CSS `attr()`, so the region's text (the result) is unchanged and announced once. They show the service's values from the first frame and tumble for 0.6 s in CSS; nothing is rolled in the UI. Off under reduced motion or by the setting.
6. **Summary**: hit dice as "N of M (dX)", an Inspiration checkbox and one-point hit-point buttons in a second `<dd>`, through the existing confirmed `character.play` command. (The outlined roll buttons and the one-row d20 mode shipped before this ADR, under D17's rules.)

## Consequences

- SPEC P-03 gains one sentence: the palette is TomeStack's own, three themes in the system colours.
- Accessibility checklist item 5 is rewritten with the computed table; items 6, 16, 29 and 30 need the owner's re-check with the sidebar collapsed and expanded, under each theme and under High Contrast; new rows cover the sidebar, Settings and the dice motion (2.3.1, 2.3.3).
- jsdom applies no CSS and does not hit-test, so layout and clickability are verified in headless Edge, by the smoke run and by the owner's zoom check. Nothing in `src/Ui` calls `matchMedia`.
- One e2e assertion changed: the shell's first Tab stop is the sidebar toggle.

## Alternatives considered

- **A 3D dice library** (`@3d-dice/dice-box`, MIT, 11.4 MB unpacked plus Babylon): a runtime dependency with a license review, runtime assets and WebAssembly that the CSP blocks (`script-src 'self'`, no `'wasm-unsafe-eval'`), WebGL in WebView2, untestable in jsdom. Rejected.
- **JavaScript interim faces** before the dice settle: timers, state churn and fake timers in tests for little gain. Deferred.
- **A portal for the sheet bar**: breaks `within(article)` in the e2e helpers and the print rule's direct-child dependency. Rejected for subgrid.
- **A collapsed sidebar rail, or a drawer**: a second focus region, or every sidebar action behind an extra step. Rejected for the header toggle.
- **A settings command in the service**: preferences of this machine do not belong in the library. Page storage, as before.

## Evidence

`src/Ui/src/settings.test.ts`, `SettingsPanel.test.tsx`, `RollResult.test.tsx`, `SheetSummary.test.tsx`, `CharacterSheet.test.tsx` (article children), the e2e flow (sidebar, settings, Tab order, every roll regex unchanged), the headless-Edge hit test (in the PR description), and the contrast table in `accessibility-checklist.md` item 5.
