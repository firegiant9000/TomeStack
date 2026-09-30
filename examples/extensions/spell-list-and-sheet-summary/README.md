# Sample extension: spell list and sheet summary

The external sample extension for M6 (ADR-011, option A: declarative only). No code runs: TomeStack reads the two
transform documents with its own bounded interpreter.

- `extension.json`: the manifest (extension API 1, runtime `declarative`, four permissions, two hooks).
- `transforms/spells-from-csv.json`: import hook. One draft spell per row of a CSV file shaped like `sample-spells.csv`.
- `transforms/sheet-markdown.json`: export hook. A Markdown summary of a character's sheet, with every license notice.
- `sample-spells.csv`: original test spells to import. Not part of the extension.

All text here is original to TomeStack; there is no SRD or third-party rules text (SPEC Q-03).

## Build the extension file

An extension is a ZIP file named `*.tomestack-ext.zip` holding `extension.json` and `transforms/` at its root, nothing
else (not this README, not the CSV). In a terminal, from this folder (`tar` comes with Windows 10 and 11):

```powershell
tar -a -c -f spell-list-and-sheet-summary.tomestack-ext.zip extension.json transforms
```

`Compress-Archive -Path extension.json, transforms -DestinationPath spell-list-and-sheet-summary.tomestack-ext.zip` works too.

Then install it from the Extensions screen. See `docs/authoring/extension.md` for writing your own.
