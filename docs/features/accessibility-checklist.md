# Accessibility checklist (sheet, builder, import)

SPEC P-03, Q-04 · BACKLOG B17 · LIVING_SPECS D05

**Formal target: WCAG 2.2 Level AA for the character sheet and the builder** (owner decision D05, 2026-09-26). The import preview and the export panel are held to the same bar. Each item lists the WCAG 2.2 success criteria it covers. An item is only ✅ when it meets AA; ⚠️ and ☐ items are open AA gaps and must be closed before MVP (MVP.md release checks).

Status key: ✅ verified (how) · 🔧 found and fixed in this change · ⚠️ open finding · ☐ not checked yet.

## Automated evidence

- `src/Ui/e2e/flow.e2e.tsx` (Vitest + Testing Library against the real DevHost) drives create → sheet → override → export → import **only through accessible roles and names**. A control without a usable label would fail the test.
- The same file checks keyboard-only access: Tab reaches "New character" and "Import package…", and Enter opens the form with focus on Name.
- M2 item 1: the builder test drives create → choices → cancelled level-up → level-up to 3 → "Make choices" by role and name only (radio groups, choice groups named by their legends), and asserts focus on the builder heading after a step change (2.4.3). Keyboard-only and Narrator passes of the builder are still owner checks (items 3 and 15).
- M2 item 2: the same test spends a resource, sets temporary hit points, takes damage, toggles a condition and rolls (feature and d20 with advantage), all by role and name. Panels are regions named by their headings. The roll result is a polite live region (`aria-live`), not `role="status"`, so it does not compete with the app's status line. Its announcement by Narrator is not checked yet (item 15).
- M2 items 3–5: the long-rest proposal, the equipment panel, the homebrew studio (source form, editor, rule groups named "Rule N: …", Check results) and the update review (captioned tables) are driven by role and name in the e2e tests. The rest proposal, editor and review move focus to their headings. The studio editor is long; a keyboard walkthrough of it is an owner check.
- M2 item 6: the Sources screen names each source's list item, and removal asks in an `alertdialog` named "Remove <file>?". The PDF viewer window is WebView2's own viewer; its accessibility is Microsoft's, and a Narrator check of it is still open.
- M2 item 7: the campaign form (named "Campaign"), the builder's "Campaign sources" group and the disabled options, which say "not allowed in this campaign" in text rather than only greyed out (1.4.1), are driven by role and name in the e2e test.
- M3 C4 and C5, M4 D5 (2026-09-28):
  - The print preview, "Report a gap" and the notes of all characters are driven by role and name, and so is the import review. That covers the "Read the text of …" region, the job progress (a polite live region in every state, so "completed" is announced as well), search results, and the "Candidates from …" region with labelled page, kind, confidence and status filters.
  - Each candidate is a region named "Candidate: <name>". Choosing one moves focus to its heading (2.4.3). Accepting or ignoring one that then leaves the filtered list returns focus to the "Candidates from …" heading instead of `body` (review 2026-09-28; the e2e flow asserts it). Closing the print preview returns focus to "Print…".
  - The confidence is text ("72 %", "a hint, not a check"), and unsure fields say "(unsure)", so colour is not the only signal (1.4.1).
  - Blockers are listed ("Blocks accepting") beside the disabled "Accept as a draft", and "Accept as reference" stays available.
  - A keyboard-only and Narrator pass of the review screen is an owner check (items 15 and 3).

## Checklist

| # | Check | WCAG 2.2 | Status |
| --- | --- | --- | --- |
| 1 | Every form control has a visible, programmatic label | 1.3.1, 3.3.2, 4.1.2 | ✅ partial: the e2e test finds every control on its path by role and name. Controls off that path (for example the source keep/use choice in the import preview) are covered by code review only |
| 2 | Radio groups and checkbox lists are grouped with a legend | 1.3.1 | ✅ code review (`fieldset`/`legend` in the builder and the import choice) |
| 3 | All actions work from the keyboard; no keyboard traps | 2.1.1, 2.1.2 | ✅ the e2e keyboard test covers the primary actions, and the owner's manual walkthrough passed (below). Re-check the new export panel (purpose radios) by keyboard |
| 4 | A visible focus indicator | 2.4.7, 2.4.11 | ✅ `:focus-visible` 3 px outline; accent contrast is 6.2:1 (light) and 8.9:1 (dark) |
| 5 | Text contrast ≥ 4.5:1 in light **and** dark scheme | 1.4.3 | 🔧 Dark mode failed: error 2.87, accent 3.04, warn 3.45. Now `light-dark()` gives 7.8 / 8.9 / 10.1. Light mode: text 21, muted 7.0, accent 6.2, warn 5.4, error 6.5 |
| 6 | Text resizes to 200% without loss of content | 1.4.4, 1.4.10 | ☐ The layout uses `rem` and `font-size: 100%`, but the check has not been run at 200% / 400% in WebView2 |
| 7 | Errors are announced and described in text | 3.3.1, 4.1.3 | ✅ errors use `role="alert"`, status messages `role="status"` |
| 8 | Status after import stays visible | 4.1.3 | 🔧 The import summary, including the backup location, was cleared immediately by opening the character. Found by the e2e test |
| 9 | Focus moves sensibly after navigation (create → sheet, import → sheet) | 2.4.3 | 🔧 Focus used to fall back to `body` when the form unmounted. Opening a sheet now focuses its heading (`tabIndex=-1`); the e2e flow asserts it after create |
| 10 | Derived values are not noisy for screen readers | 4.1.3 | 🔧 Field values used `<output>`, which has the implicit role `status` (a live region). They are plain text in the field heading now; the sheet has 40 fields (M1 item 5) |
| 11 | No action is silently ignored | 3.2.x | 🔧 "New character" did nothing if clicked before `app.info` had loaded. It is now disabled until then; both e2e tests wait for it to become enabled |
| 12 | Override forms are independent per field | 3.3.2 | 🔧 Each field card has its own override state (item 11) |
| 13 | Tables have captions and header cells | 1.3.1 | ✅ trace, package and diff tables have `caption` and `th scope` |
| 14 | Colour is not the only signal | 1.4.1 | ✅ overrides say "overridden (calculated N)"; conflicts and warnings are text |
| 15 | Screen reader pass (Narrator) on the sheet and the import preview | 4.1.2 | ☐ |
| 16 | Windows High Contrast / forced colours | 1.4.11 | ☐ the system colours `Canvas`/`CanvasText` should adapt; not checked |
| 17 | Expandable field cards work by keyboard and name their state | 2.1.1, 4.1.2 | ☐ Native `<details>`/`<summary>` with a heading inside the summary. Check Narrator's expanded/collapsed announcement |
| 18 | The studio's class editor (M5 slice 1b) | 1.3.1, 2.4.3, 3.3.1, 4.1.3 | 🔧 review fixes, partly e2e-verified. Every control has a label, and groups have a `fieldset`/`legend`. "Create skill choice" and "Remove the skill choice" move focus to the group's legend and announce the result in a polite live region; the e2e flow asserts both. A disabled "Create skill choice" says why (`aria-describedby`). A list field that does not parse is `aria-invalid`, with its error linked (`aria-describedby`), and Save and Publish stay disabled with the reason next to them. ☐ Narrator and keyboard-only passes on the editor |
| 19 | The homebrew debugger's findings (M5 slice 2) | 1.3.1, 2.4.3, 2.4.6 | 🔧 e2e-verified in part. Findings are a named region with a list; each says its severity in words (not by colour alone) and has a "Show rule … of …" button with a unique name. **Show** moves focus to the rule's `fieldset` (named by its legend), or to the editor heading for a finding on the whole entry; the e2e flow asserts the focus. ☐ Narrator pass: check the focused fieldset is announced by its legend |
| 20 | "Try it", the draft sandbox (M5 slice 3) | 1.3.1, 3.3.1, 4.1.3 | 🔧 e2e-verified in part. A named section with labelled controls; a level that is not 1–20 is `aria-invalid` with its error linked, and "Try it" stays disabled. The result is a named region with headings and lists, and a polite live region announces it ("Tried at total level …; nothing was saved"). ☐ Narrator pass |
| 21 | "Compare revisions" (M5 slice 4) | 1.3.1, 1.4.1 | 🔧 e2e-verified in part. Labelled selects and checkboxes; results in a named region with captioned tables; each text-diff line says "added", "removed" or "unchanged" in words (visually hidden), with the + / − marks hidden from screen readers, so colour and marks are never the only cue. ☐ Narrator pass; ☐ High Contrast check of the diff colours |
| 22 | The relationship tree (M5 slice 5, B19; WAI-ARIA APG tree view) | 1.3.1, 2.1.1, 2.4.3, 4.1.2 | 🔧 e2e-verified in part: `tree`/`treeitem`/`group` roles, each item named by its own label (`aria-labelledby`, not its open children), with `aria-level`, `aria-posinset`, `aria-setsize` and `aria-expanded`. One tab stop (roving tabindex). The e2e flow Tabs into the tree and presses Up, Down, Right (open, then first child), Left (to the parent), Home, End, Space (open and close) and Enter (show the rule), checking focus and the attributes after each. Clicking an item is checked once. Relations are text, never a canvas. ☐ Narrator pass (announcement of level, position and expanded state); ☐ 200% zoom |
| 23 | Snapshots on the sheet (M5 slice 8) | 1.3.1, 2.4.3 | 🔧 e2e-verified in part: a labelled name field and buttons named by their snapshot ("Restore … …"); the restore preview is a named region with a captioned table, and focus moves to its heading when it opens (the e2e flow checks it). "Keep the current state" returns focus to the snapshot's button (implemented, not e2e-checked). ☐ Narrator pass |

## Manual keyboard walkthrough

It needs a person at the app. Procedure: launch `TomeStack.exe` and do not touch the mouse. Then:

1. Tab through the header, sidebar and main area. Note the order and anything you can't reach.
2. Create a character: type a name, change the rules family with the arrow keys, set scores, toggle content with Space, and submit with Enter.
3. On the sheet, apply an override, remove it, and export (the Save dialog must work from the keyboard).
4. Import the exported file (the Open dialog), choose a source version if asked, and apply.
5. Repeat at 200% text zoom (Ctrl + +) and in dark mode.

Record each run here as the date, build, pass/fail per step, and notes.

| Date | Build | Result | Notes |
| --- | --- | --- | --- |
| 2026-09-26 | `m0-finish` (PR #2), v0.1.0 | **PASS** (owner, Arlo Kharod) | Keyboard walkthrough steps 1–4 passed, including the native Save dialog and the import. Step 5 (200% zoom, dark mode) was not reported separately; item 6 stays ☐. Predates the export purpose panel (ADR-007), so re-run step 3 with "Share" |
