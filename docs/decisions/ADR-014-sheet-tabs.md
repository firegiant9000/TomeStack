# ADR-014: Sheet navigation: an in-sheet tablist with every panel kept mounted

Status: accepted (owner, 2026-10-03; LIVING_SPECS D17)
Date: 2026-10-03
Implementation: slice 1 of 4 (the strip with "Sheet" and "Manage"; features/sheet-layout.md). The summary (slice 2), the seven tabs with "Report a gap" opening Notes (slice 3), and the remembered tab and deep link (slice 4) follow; the Consequences and Evidence below describe the finished design.

## Context

The character sheet (`src/Ui/src/components/CharacterSheet.tsx`) was one page of about 25 sections, with the admin panels at the top and the core numbers at the bottom in collapsed field cards. The owner asked for a summary area with focused pages below, as paper sheets and other digital sheets have. SPEC P-03 forbids mimicking D&D Beyond's trade dress. There is no router: `App.tsx` keeps one `screen` in state, and the sheet is keyed by character so every play action replaces the view in place. The e2e flow (`src/Ui/e2e/flow.e2e.tsx`) queries every section by role and name and asserts focus on the sheet heading, on the gap-note text box after "Report a gap", and on buttons after Archive, Snapshot and Print.

## Decision

1. **An ARIA tablist inside the sheet** (`src/Ui/src/components/sheet/TabList.tsx`, WAI-ARIA APG tabs): roving tabindex, Left/Right wrap, Home/End, automatic activation, focus follows the selected tab only after a key press. Panels are `role="tabpanel"`, labelled by their tab, `tabIndex={-1}`.
2. **Every panel stays mounted; inactive panels carry `hidden`.** Field-card override inputs, the gap-note draft, open `<details>` and a rest in progress survive a switch; data effects run once per character; the Last roll live region lives in the always-visible summary. `display: none` removes hidden panels from the accessibility tree.
3. **Tabs:** Play (default), Spells (casters only), Inventory, Features, Stats, Notes, Manage. Admin panels (Updates available, Export, Export for a virtual tabletop, Archive, Snapshots) are on Manage. Open choices and content problems stay above the tabs (SPEC C-03).
4. **The last tab is remembered per character** in the page's own storage (`tomestack.sheetTab.<characterId>`, the WebView2 profile in the data folder; in no package or backup), the pattern of `designFeedback.ts`. A deep link (`initialTab`, used by the Gap notes screen) wins over the memory.

## Consequences

- Testing Library's default queries skip hidden elements, so e2e tests open the tab a user would (`openTab`). This made about 40 query sites explicit about where a section lives.
- The summary must never carry a heading or region whose name matches a panel's (two regions with one name break `getByRole` and confuse screen readers). It is a `<dl>`.
- Print is unchanged: the print view is its own layout and a direct child of `article.panel`.
- Open accessibility checks: a Narrator pass of the strip and a 200%/400% zoom check (checklist items 6, 15, 29).

## Alternatives considered

- **Sub-screens in `App`'s `screen` union:** nine `setScreen` call sites would carry the tab, the sheet would unmount per tab and lose typed state, and every play action already replaces the view.
- **A hash router:** no URL state exists anywhere, the WebView2 page is a virtual host, and it adds a dependency for one screen. Revisit if more screens gain sub-pages.
- **A menu for the admin panels:** a new menubutton pattern, and it breaks the focus-return assertions of Archive and Snapshots.

## Evidence

`src/Ui/src/components/sheet/TabList.test.tsx` (roles, keyboard, hidden panels); the e2e flow (every sheet test opens its tabs; "Report a gap" switches to Notes and focuses the text box (slice 3)); `docs/features/sheet-layout.md`.
