# ADR-017: Character import from text

Status: accepted (owner decision D37, 2026-10-14; built in Wave 2)
Date: 2026-10-18

## Context

Tester feedback item 19a: some players keep a character as notes, not as a filled-in sheet. D37 scopes the first slice to pasted text and `.txt` / `.md` files. A PDF without form fields is a second slice (item 19b, built after it: ADR-013 records it, with a local-drive check on dialog paths). No `.docx`, no network.

## Decision

- **Same sheet, same steps.** `TextSheetParser` turns text into the `DdbSheet` the PDF layouts produce, with a synthetic layout (`text`, outside `LayoutMaps.All`) that carries neutral wording for the override reason and notes. Everything after reading (the token hold, matching, numbers, summary, apply, and the Wave 2 additions: custom items, coins, details, notes, the stub class, reference drafts) is the PDF path, unchanged.
- **In the service, no worker.** Text is a string; there is nothing to isolate, and nothing is written to disk. The command is `ddb.readText { text }` in the closed command set.
- **Bounded, with counts only.** 262,144 UTF-16 characters (`ddb.text-too-large`), 5,000 lines (`ddb.text-too-many-lines`), 2,000 characters per line (a longer line is listed, clipped, as not read), and a 2 second budget checked between lines (`ddb.text-timeout`). Every regular expression is `NonBacktracking`. The size and line checks come before any parsing. A refusal and every `ToString` carry counts, never text.
- **Total and tolerant.** The parser never throws on any input; a line that no rule reads is kept as a "Not read" entry with its line number instead of being guessed. A duplicate label, an unreadable class or level, or an out-of-range score is unreadable and its line is not read.
- **Markdown is stripped, not rendered.** Heading marks, bold marks and list markers are removed; text is displayed as plain React text only.
- **Not read lines are the user's call.** They are listed in the summary and, by default, kept as session notes ("Not read from the text (part n of m)"). `keepNotRead` is part of the preview and apply requests, so changing it re-previews and the stale-preview hold applies. Session notes are private: a share package never carries them.
- **The browser checks the file size first.** `file.size` is compared with the limit before `file.text()`; over it, the file is refused and only sizes are named.

## Consequences

- A pasted character reaches the same review as a PDF, with the same consent steps; nothing is written before "Create character".
- The format is forgiving by design, so a misread is possible: the lines not read and the comparison step are the safety net, and the user sees both before anything is saved.
- A PDF without form fields (item 19b) uses this same parser: `ddb.read` falls back to the PDF's page text when the form reader reports no fields, so the caps, refusals, "Not read" lines and privacy rules here apply to it unchanged (the layout is named "text from the PDF"). See ADR-013.

## Alternatives considered

- A strict template: rejected; players do not write that way.
- Parsing in the import worker: rejected; no file, no native code, nothing to contain.
- Guessing unreadable lines into fields: rejected; a listed line costs a glance, a wrong field costs trust.

## Evidence

- `tests/AppService.Tests/TextSheetParserTests.cs` and `Properties/TextSheetParserPropertyTests.cs` (labels, shapes, caps, timings, refusals, share-marker, whole import).
- `src/Ui/src/components/ddb/DdbImportPanel.test.tsx` and the e2e flow "imports a character from pasted text".
