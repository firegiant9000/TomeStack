# Accessibility checklist (sheet, builder, import)

SPEC P-03, Q-04 · BACKLOG B17 · LIVING_SPECS D05

**Formal target: not set. It is an owner decision (D05).** Until one is chosen, this checklist uses the working default from LIVING_SPECS, "full keyboard use, text zoom, contrast check". Each item lists the WCAG 2.2 success criterion it maps to, so adopting a target (for example WCAG 2.2 AA) only means confirming the list rather than rewriting it.

Status key: ✅ verified (how) · 🔧 found and fixed in this change · ⚠️ open finding · ☐ not checked yet.

## Automated evidence

- `src/Ui/e2e/flow.e2e.tsx` (Vitest + Testing Library against the real DevHost) drives create → sheet → override → export → import **only through accessible roles and names**. A control without a usable label would fail the test.
- The same file checks keyboard-only access: Tab reaches "New character" and "Import package…", and Enter opens the form with focus on Name.

## Checklist

| # | Check | WCAG 2.2 | Status |
| --- | --- | --- | --- |
| 1 | Every form control has a visible, programmatic label | 1.3.1, 3.3.2, 4.1.2 | ✅ e2e test finds every control by role and name |
| 2 | Radio groups and checkbox lists are grouped with a legend | 1.3.1 | ✅ code review (`fieldset`/`legend` in the builder and the import choice) |
| 3 | All actions work from the keyboard; no keyboard traps | 2.1.1, 2.1.2 | ✅ partial: the e2e keyboard test covers the primary actions. The full walkthrough is below (☐) |
| 4 | A visible focus indicator | 2.4.7, 2.4.11 | ✅ `:focus-visible` 3 px outline; accent contrast is 6.2:1 (light) and 8.9:1 (dark) |
| 5 | Text contrast ≥ 4.5:1 in light **and** dark scheme | 1.4.3 | 🔧 Dark mode failed: error 2.87, accent 3.04, warn 3.45. Now `light-dark()` gives 7.8 / 8.9 / 10.1. Light mode: text 21, muted 7.0, accent 6.2, warn 5.4, error 6.5 |
| 6 | Text resizes to 200% without loss of content | 1.4.4, 1.4.10 | ☐ The layout uses `rem` and `font-size: 100%`, but the check has not been run at 200% / 400% in WebView2 |
| 7 | Errors are announced and described in text | 3.3.1, 4.1.3 | ✅ errors use `role="alert"`, status messages `role="status"` |
| 8 | Status after import stays visible | 4.1.3 | 🔧 The import summary, including the backup location, was cleared immediately by opening the character. Found by the e2e test |
| 9 | Focus moves sensibly after navigation (create → sheet, import → sheet) | 2.4.3 | ⚠️ Focus falls back to `body` when the form unmounts. Move focus to the sheet heading |
| 10 | Derived values are not noisy for screen readers | 4.1.3 | ⚠️ `<output>` in each field heading has the implicit role `status` (a live region). Every recalculation may be announced. Decide on this deliberately once there are more fields |
| 11 | No action is silently ignored | 3.2.x | ⚠️ "New character" does nothing if clicked before `app.info` has loaded. Disable it until ready, or render the form with a loading state |
| 12 | Override forms are independent per field | 3.3.2 | ⚠️ All fields' override inputs share one state. That is harmless with one field, but wrong now that the sheet shows several fields (item 11) |
| 13 | Tables have captions and header cells | 1.3.1 | ✅ trace, package and diff tables have `caption` and `th scope` |
| 14 | Colour is not the only signal | 1.4.1 | ✅ overrides say "overridden (calculated N)"; conflicts and warnings are text |
| 15 | Screen reader pass (Narrator) on the sheet and the import preview | 4.1.2 | ☐ |
| 16 | Windows High Contrast / forced colours | 1.4.11 | ☐ the system colours `Canvas`/`CanvasText` should adapt; not checked |

## Manual keyboard walkthrough

**Not yet performed.** It needs a person at the installed app. Procedure: launch `TomeStack.exe` and do not touch the mouse. Then:

1. Tab through the header, sidebar and main area. Note the order and anything you can't reach.
2. Create a character: type a name, change the rules family with the arrow keys, set scores, toggle content with Space, and submit with Enter.
3. On the sheet, apply an override, remove it, and export (the Save dialog must work from the keyboard).
4. Import the exported file (the Open dialog), choose a source version if asked, and apply.
5. Repeat at 200% text zoom (Ctrl + +) and in dark mode.

Record each run here as the date, build, pass/fail per step, and notes.

| Date | Build | Result | Notes |
| --- | --- | --- | --- |
| | | | |
