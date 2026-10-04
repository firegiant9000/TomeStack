# M3 C4: printable backup

MVP "Separate replacement milestone (M3)" ("printable backup") · SPEC P-03 · owner decision 2026-09-28: a print view of the sheet, printed with WebView2's own print dialog, with gap notes opt-in · status: **implemented**.

UI: `src/Ui/src/components/PrintView.tsx`, opened by "Print…" in the sheet header (`CharacterSheet.tsx`), and the `@media print` rules in `src/Ui/src/styles.css`. Acceptance: the e2e test "prints a sheet with its license notices, and gap notes only when ticked".

## What it is

A paper (or "Microsoft Print to PDF") copy of the character to play from if the computer is not at the table. It is not a restorable backup: that is the personal backup package (ADR-007). It is a print-only layout of the sheet, not a PDF layout engine. Printable and condensed PDF sheets and cards stay M7 (BACKLOG B14).

## Flow

1. **Print…** in the sheet header opens the "Print preview" region at the top of the sheet, above the summary and the tabs (ADR-014). Focus returns to the button when it closes.
2. **Include gap notes** is off by default, because notes may describe private homebrew (`gap-notes.md`).
3. **Print…** in the preview calls `window.print()`. WebView2 shows its print dialog, and the printed page contains only the preview, because the print CSS hides the rest of the app.

## Contents

- The name, rules family, level, classes and campaign.
- Ability scores, modifiers and saving throws, and the skills.
- Proficiency bonus, Armor Class, initiative, hit points (current, maximum, temporary), hit dice left, conditions, exhaustion and inspiration.
- Attacks (to hit, damage, properties), and resources (current of maximum, and when each recovers).
- Spell slots and Pact Magic left, and each caster's attack and DC with its spells.
- Every feature: its name, source and page, automation status and text. Each non-automatic effect's text is printed as "By hand:" (the manual step).
- Overrides, with the calculated value and the reason.
- Gap notes, only when ticked.
- A footer: the TomeStack version, the date, and each source's license, attribution and modification notice. It comes from `package.exportPreview` (backup), which writes nothing, so CC-BY attribution travels with printed SRD text.

## Privacy and offline

- It opens no socket and makes no request (ADR-006). The page never receives or prints a local path or an attachment file name: sources are cited by title and page only.
- It sends nothing anywhere. Printing is the operating system's job, and the printer and its destination are the user's choice.
- The e2e test checks that the preview text has no Windows or user path, and that a gap note appears only after it is ticked.

## Not verified automatically

- **What the WebView2 print dialog shows and prints** in the desktop app. jsdom cannot print. The e2e test proves the preview's contents and that "Print…" calls `window.print()`. That the shell's dialog opens and the printed page shows only the preview is an owner check on the installed app.
- Page breaks on long feature texts. Each feature avoids breaking inside where the printer allows it.
