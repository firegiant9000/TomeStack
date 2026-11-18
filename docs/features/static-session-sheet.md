# Static session sheet (D105)

Wave 4 item 4.8 · decision [D105](../LIVING_SPECS.md) · status: **built; the owner's check on a real phone is not recorded yet.**

A sheet for the table when you would rather not carry a laptop. It is one HTML file you send to a phone or tablet and open in the browser. It needs no internet, no app and no TomeStack. It is a second target of the export adapters ([export-adapters.md](export-adapters.md)); the printable sheet (Print… on the character sheet) stays the full backup.

## Making one

1. Open a character, then **More**, **Export**.
2. Under "Export to another tool or device", choose **Sheet for a phone or tablet (HTML page, no internet needed)**.
3. Choose the purpose, as for the other formats:
   - **Share** keeps only totals from content that may not be shared, and leaves out your session notes.
   - **Personal copy** includes your own homebrew and your session notes, and is labelled "Do not share it".
4. Read the preview and save. The Save dialog offers "HTML page". The file is named `<name>.tomestack-sheet.html`.

A share copy goes through the same preflight as the other exports. The preview adds two warnings: "Includes N session notes." (personal copy) and "Ticks on the phone are not saved: reopening the file starts from this export."

## What is in the file

A header (name, classes, level, rules family, export date and the purpose label); Armor Class, initiative and proficiency bonus; hit points printed, with two empty write-in fields; abilities and saves; skills; conditions and exhaustion; death saves (pre-ticked from the play state); weapon attacks; spellcasting, with slots as checkboxes and each spell in a collapsible block; resources (up to 20 uses as checkboxes, more as a write-in field, and "no fixed maximum" as text); hit dice; toggles as read-only text; class columns; features in collapsible blocks; session notes (personal copy only); and the sources, licence notices and what was left out.

It follows the phone's light or dark setting. Printing it shows summaries only.

## What it does not do

- **Ticks are not saved.** They are plain checkboxes. Close the page or reload it and everything goes back to the export. Nothing writes back to TomeStack.
- **No script, no network.** The page loads nothing from anywhere and has no link that leaves it (links go only to places inside the page).
- It does not recalculate. Change the character in TomeStack and export again.
- The sheet export model v1 is unchanged; this target only reads it.

## Safety rules

1. **Every text is HTML-encoded**, including homebrew names and descriptions and your notes.
2. **A tag-level self-check** (`HtmlSheet.Validate`, a small hand-written tokenizer) runs on the finished page before anything is written. It refuses scripts, event attributes, `src`, any link that is not `#...`, `style` attributes, embedding tags (frames, objects and the like), `@import` and `url(`. A failure is `export.invalid-output`, as for the other adapters.
3. **A Content-Security-Policy meta tag** blocks everything except the page's own style block (matched by hash). It is a backstop only: many phone viewers ignore it, so the self-check is the real guard.
4. **The privacy scan** that every export gets (data folder, profile, linked PDFs, the Windows user name as a path segment) covers the HTML, the sheet JSON it came from and the raw note text.

## Tests

- `tests/AppService.Tests/HtmlSheetTests.cs`: the content of each section, encoding of hostile text, and the self-check refusing each forbidden construct.
- `tests/AppService.Tests/ExportAdapterTests.cs`: the golden file `Golden/multiclass-caster.tomestack-sheet.html` (open it in any browser to see the result), notes only in a personal copy, a local path refused, and the Save dialog filter.
- `src/Ui/src/components/VttExportPanel.test.tsx`: the choice in the export panel.
- `src/Ui/a11y/static-sheet.a11y.tsx`: axe in Edge, light and dark.

**Not verified:** opening the file on a real phone or tablet, and a Narrator or screen-reader pass.
