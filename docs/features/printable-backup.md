# M3 C4: printable backup

MVP "Separate replacement milestone (M3)" ("printable backup") · SPEC P-03 · owner decision 2026-09-28: a print view of the sheet, printed with WebView2's own print dialog, with gap notes opt-in · status: **implemented**.

UI: `src/Ui/src/components/PrintView.tsx`, opened by "Print…" in the sheet header (`CharacterSheet.tsx`), and the `@media print` rules in `src/Ui/src/styles.css`. Acceptance: the e2e test "prints a sheet with its license notices, and gap notes only when ticked".

## What it is

A paper (or "Microsoft Print to PDF") copy of the character to play from if the computer is not at the table. It is not a restorable backup: that is the personal backup package (ADR-007). It is a print-only layout of the sheet, not a PDF layout engine. Printable and condensed PDF sheets and cards stay M7 (BACKLOG B14).

## Flow

1. **Print…** in the sheet header opens the "Print preview" region after the summary, in the main column (ADR-015). The preview scrolls to the top of the window and focus moves to its "Print character" heading; focus returns to the button when it closes.
2. **Include gap notes** is off by default, because notes may describe private homebrew (`gap-notes.md`). **Include session notes (private)** is off by default too (D22), and so is **Include details (personality, appearance, backstory, languages and tools)** (item 17, D36).
3. **Print…** in the preview calls `window.print()`. WebView2 shows its print dialog, and the printed page contains only the preview, because the print CSS hides the rest of the app.

## Print options: combat card and large print (P14, D71)

The preview opens with a "Print" group (native radios) and a "Large print" checkbox. They are page state only: nothing is stored, and reopening the preview starts on "Full sheet" with Large print off.

- **Large print** prints at 150% of the base size, or at the user's text size when that is larger, at most 200% (so 150%, 175% or 200%). `PrintView` sets `--print-font-size` on the root while the box is ticked, and the `@media print` rule reads it (`var(--print-font-size, 100%)`); the section also carries `print-large`. Unticked, or when the preview closes, the variable is removed and the print is 100% as before.
- **Combat card** replaces the full layout with one page, built from `sheet.fields` ids (`armorClass`, `hitPoints`, `speed`, `initiative`, `proficiencyBonus`, `save.*`, `passive.perception|insight|investigation`) and the existing attack, resource, spell slot, pact slot and toggle lists, so it adds no calculation: Armor Class, hit point maximum with blank current and temporary boxes, speed, initiative, proficiency bonus, saves, passives, attacks (to hit, damage), resources and spell slots (one tick box per point), toggles as named boxes and a blank conditions list. The header and the footer (version and license notices) stay. A field the sheet does not have prints "—", not 0. A pool or slot level above 20 (`maxTickBoxes`) prints "N uses" or "N slots" and one write-in box instead of N boxes. Notes, gap notes, details and their tick boxes are absent in this mode, so none of that text can reach the card.
- **Overflow:** the header, card and footer are laid out as one block 7.27in wide (A4 less 0.5in margins) at the print font size (header and footer sizes in `em`, so they follow it), measured with their margins on screen and again on `beforeprint`, and fitted in 10in (Letter less 0.5in margins) less a small allowance. The core (vitals, saves, passives, attacks, resources) is never dropped. If it does not fit, `fitCombatCard` drops the conditions list first, then the toggles, then the spell slots (with Pact Magic) last, and the card ends "Continued on the full sheet." The font is never reduced; if the core alone is too tall it spills to a second page with the same line. The shell's actual page margins are the print dialog's, so the 10in is an estimate (owner check).

## Contents

- The name, rules family, level, classes and campaign.
- Ability scores, modifiers and saving throws, and the skills.
- Proficiency bonus, Armor Class, initiative, hit points (current, maximum, temporary), hit dice left, conditions, exhaustion and inspiration.
- Attacks (to hit, damage, properties), and resources (current of maximum, and when each recovers).
- Spell slots and Pact Magic left, and each caster's attack and DC with its spells.
- Every feature: its name, source and page, automation status and text. Each non-automatic effect's text is printed as "By hand:" (the manual step).
- Overrides, with the calculated value and the reason.
- Gap notes, only when ticked.
- Session notes, only when ticked (D22).
- Details, only when ticked (D36): a "Details" list of each field that has text (label and text, line breaks kept), with the languages and the tools as comma-separated lines. Nothing prints if no detail is set.
- A footer: the TomeStack version, the date, and each source's license, attribution and modification notice. It comes from `package.exportPreview` (backup), which writes nothing, so CC-BY attribution travels with printed SRD text.

## Privacy and offline

- It opens no socket and makes no request (ADR-006). The page never receives or prints a local path or an attachment file name: sources are cited by title and page only.
- It sends nothing anywhere. Printing is the operating system's job, and the printer and its destination are the user's choice.
- The e2e test checks that the preview text has no Windows or user path, and that a gap note appears only after it is ticked.

## Not verified automatically

- **What the WebView2 print dialog shows and prints** in the desktop app. jsdom cannot print. The e2e test proves the preview's contents and that "Print…" calls `window.print()`. That the shell's dialog opens and the printed page shows only the preview is an owner check on the installed app.
- Page breaks on long feature texts. Each feature avoids breaking inside where the printer allows it.
