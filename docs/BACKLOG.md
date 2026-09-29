# Approved feature backlog · v0.1

All 20 ideas from discovery are included here. A target milestone indicates planning order, not a guarantee. IDs make future spec edits traceable.

| ID | Feature | Earliest target | Acceptance seed |
| --- | --- | --- | --- |
| B01 | Calculation inspector | M1/M2 | A number shows every applied rule and override |
| B02 | Homebrew debugger | M5 | Missing references, invalid formulas and dead resources have diagnostics. **Done 2026-09-29 (M5 slice 2, fixture-verified):** `content.diagnose`, [features/homebrew-studio.md](features/homebrew-studio.md#the-homebrew-debugger-m5-slice-2-b02) |
| B03 | Character sandbox | M5 | Preview a draft subclass at chosen levels without changing a saved character. **Done 2026-09-29 (M5 slice 3, fixture-verified):** "Try it", `content.sandbox`, [features/homebrew-studio.md](features/homebrew-studio.md#try-it-the-draft-sandbox-m5-slice-3-b03) |
| B04 | Before/after tests | M5 | Compare two revisions on fixture characters. **Done 2026-09-29 (M5 slice 4, fixture-verified):** `content.compare` runs both revisions on copies of your characters and on a blank character (the stand-in for fixtures, which the shipped app does not have) |
| B05 | PDF source-page links | M2 | Open cited page offline. **Implemented (M2 item 6, `features/pdf-attachments.md`)**; the owner check of the landed page passed on 0.2.2 (reported by the owner, 2026-09-28) |
| B06 | Rules-family compatibility checker | M1/M2 | Warn and record exception for deliberate mix |
| B07 | Homebrew diff viewer | M3/M5 | Compare revisions by mechanics and text. **Done 2026-09-29 (M5 slice 4, fixture-verified):** "Compare revisions", [features/homebrew-studio.md](features/homebrew-studio.md#compare-revisions-diff-and-beforeafter-m5-slice-4-b07-and-b04) |
| B08 | Character snapshots | M3/M5 | Restore an earlier state with a preview |
| B09 | Command palette | M6 | Search a command and execute it from keyboard |
| B10 | PDF full-text search | M7 | Search imported text and open matching page |
| B11 | Tags/folders/collections | M6 | Filter content and characters by custom labels |
| B12 | Local campaign profiles | M2 | Filter sources and rule choices per campaign. **Implemented (M2 item 7, `features/campaigns.md`)** |
| B13 | Shareable campaign packs | M6 | Export/import rules and permitted assets safely |
| B14 | Spell/item/feature cards | M7 | Produce readable quick-reference cards |
| B15 | Homebrew templates | M5 | Start a resource, transformation or class pattern. **Done 2026-09-29 (M5 slice 6, fixture-verified, provisional set):** four draft-only templates, [features/homebrew-studio.md](features/homebrew-studio.md#templates-m5-slice-6-b15-provisional) |
| B16 | Optional local-language assistant | M7 | Turn text into reviewable suggestions without requiring AI |
| B17 | Accessibility and customization | M2 baseline, M7 depth | Keyboard, scaling, high contrast and layout checks |
| B18 | Themes | M7 | Offer modern, dark and tabletop themes |
| B19 | Relationship graph | M5 | Navigate class → feature → resource dependencies. **Done 2026-09-29 (M5 slice 5, fixture-verified):** "Show relationships", a keyboard tree, [features/homebrew-studio.md](features/homebrew-studio.md#relationships-the-content-tree-m5-slice-5-b19) |
| B20 | Foundry/Roll20 export adapters | M6+ | Document supported scope and validate generated file |

## Other approved directions

- Full custom base classes (M5); homebrew monsters and DM material (M7); PDF whole-book and selection-based interpreted import (M4); optional progression balance feedback with an off switch (M5/M7); printable and condensed character PDFs (M7); documented JSON-based character/homebrew files (M2); future extension SDK (M6).
- Keep later features behind separate acceptance plans so that shipping the MVP does not imply they exist.
