# Extensions (ADR-011, option A)

SPEC P-05 · ROADMAP M6 slice 3 · [ADR-011](../decisions/ADR-011-extension-api.md) (accepted, option A: declarative only) · status: **implemented and fixture-verified on the unmerged M6 slice 3 PR (2026-09-29)**; database v9 and library-backup format v9 (provisional) wait for the owner's approval.

An extension adds an **import** (a JSON or CSV file becomes draft content) or an **export** (a character's sheet, or published content, becomes a text or JSON file). **No extension code ever runs.** An extension is data: a manifest and one transform document per hook, which TomeStack's own bounded interpreter reads (`AppService/Extensions/DeclarativeTransform.cs`). The shipped app still opens no socket and makes no network call (ADR-001, ADR-006).

## The file

A `*.tomestack-ext.zip` holding exactly:

```text
extension.json               the manifest (docs/schemas/extension-manifest.v1.schema.json)
transforms/<hook id>.json    one transform per hook (docs/schemas/extension-transform.v1.schema.json)
```

It is read as untrusted input: at most 5 MB and 64 entries, 1 MB per entry and 16 MB unpacked, every path on that allowlist (anything else, `..` included, is refused before anything is unpacked), nothing extracted to disk, every transform parsed before install, and no entry left unused. The sample is [examples/extensions/spell-list-and-sheet-summary](../../examples/extensions/spell-list-and-sheet-summary/).

A source pack, campaign pack or character package can never carry an extension, and importing one never installs one (`package.extension-not-allowed`).

## Manifest

`format` `tomestack.extension`, `formatVersion` 1, `id` (a UUID), `name`, `version` (major.minor.patch), `author`, `license` (required), optional `homepage` (shown as text, never opened) and `description`, `extensionApi` (1; `app.info.extensionApi` lists what a build runs), `runtime` (`declarative`), `permissions[]` and `hooks[]` (`kind` import or export, `id`, `label`, `transform`, and `accepts` json/csv for imports, `produces` text/json and optional `fileExtension` for exports). A newer format or API, another runtime, an unknown permission or an unknown field refuses the install, and nothing is partly loaded.

## Permissions

| Permission | Grants |
| --- | --- |
| `read.sheet` | The sheet export model of a character you pick when you run it |
| `read.content` | Published revisions of sources you pick when you run it, filtered by the run's purpose |
| `import.file` | One JSON or CSV file you pick in the native Open dialog (at most 5 MB); TomeStack parses it |
| `write.drafts` | Draft content in a new source the run creates |
| `export.file` | One output file, written through the native Save dialog |

Never, under any runtime: network, file system, clipboard, settings, other extensions, backups, gap notes, PDFs, extracted text, or running without you.

- **Install** (`extension.installPreview` / `extension.installChoose`, then `extension.install { token, grants, confirm }`): the preview shows the manifest, each permission in words and the hooks, and installs nothing. Permissions start unticked; you grant a subset, and a hook whose permissions are not all granted cannot run. The file is kept read-only at `<data dir>/extensions/<sha256>.zip`.
- **Grants are bound to the file's SHA-256.** Installing another file of the same extension, even with the same permissions, is an update that asks again and shows what changes; one by a different author is refused (`extension.author-changed`). A file changed on disk after it was granted does not run (`extension.file-changed`).
- **Turn off** (`extension.setEnabled`) and **remove** (`extension.remove`, confirmed): removing revokes everything and deletes the file; drafts it made stay.
- **Review and grant** (`extension.review`): the install preview of an installed extension, read from its stored file, so its permissions can be granted again without the original file (for example after a full restore brought it back ungranted). Installing the same file again also repairs a stored copy that was damaged.

## Running a hook

Every run is started by you, shows a preview (`extension.runPreview`), and writes only what the preview showed, once (a one-use token; a run whose extension was turned off, removed or replaced after the preview, or that used a permission since withdrawn, is refused).

- **Import** (`import.file` + `write.drafts`): the file is parsed by TomeStack (JSON nested at most 32 levels; CSV with RFC 4180 quoting, lines ending in CRLF, LF or a bare CR, at most 50,000 rows, 10,000 characters per field, 200 columns, a header of distinct names, blank lines skipped unless there is one column; a CSV becomes `{ "rows": [ { header: value } ] }`). The transform's output is a list of drafts: each item gives `kind` (feat, spell, item or feature), `name`, `summary`, `effects` and optionally `rulesFamilies`; TomeStack sets the ids, the source, the status (draft) and the schema version, and ignores anything else with a warning. Each draft is checked as `content.saveDraft` checks it, and the preview shows its validation. `extension.runImport` saves them in one transaction into a **new source**: `redistributable: false`, license "Personal homebrew", origin local, and **import-derived** for good (M6 slice 1), so it is never shared and nothing is active until you publish it.
- **Export** (`export.file` + `read.sheet` or `read.content`): the input is `{ "sheet": <sheet export model v1> }` and/or `{ "content": [ published revisions ] }`, both filtered by the run's **purpose** (ADR-007 item 11): `share` (the default) drops content that may not leave and keeps its totals; `personal` also lets out your own homebrew. The output (text, or JSON written indented) is at most 5 MB and is **scanned before anything is written** for the data-folder path, the user-profile path and every linked-PDF path, and for the Windows user name as a path segment (`…/<name>/…`; not inside ordinary words, so a user named Max can still export "maximum"). The scan ignores case and Unicode compatibility forms, drops invisible format characters, treats both slash directions and their lookalikes alike, collapses doubled slashes, and reads JSON with its escapes decoded (`extension.output-refused`). `extension.runSaveAs` writes it through the native Save dialog and keeps the preview if the dialog is cancelled; `extension.runExport` returns it as base64 in browser development. A run the source picker or character picker did not ask for is never read: an export hook reads a character only with `read.sheet`, and sources only with `read.content`.
- Error messages name the extension, the hook and a code, never the input's or the output's text.

## The sheet export model v1

`docs/schemas/sheet-export.v1.schema.json`, built by `AppService/Exports/SheetExport.cs` from the computed sheet: the character (name, rules family, level, classes with hit die and subclass), abilities with saves, skills with proficiency (none, proficient, expertise), the totals Armor Class, initiative, proficiency bonus, hit points, spell attack and save DC (each with its automation status and whether it is overridden, never the reason), hit points, hit dice, slots, casters with their spells, resources, attacks, features with their texts, toggles, scales, conditions and exhaustion, what was dropped (by source and count) and a notice for every contributing source. Each item carries its content and revision ids and its source's title (ADR-012's provenance flag). The schema is an allowlist with `additionalProperties: false` throughout, so no path, attachment id, gap note, override reason or trace can be in it.

## The transform language

A transform is `{ "tables"?: { name: { key: value } }, "output": expression }`. An expression is a JSON literal or an object with exactly one operator:

| Operator | Does |
| --- | --- |
| `const` | The JSON value as written |
| `get`, `getRoot` | A JSON Pointer into the current item or the whole input (`""` is the item itself) |
| `template` | Text with `{/pointer}` (current item) and `{#/pointer}` (input) placeholders; `{{` and `}}` are braces |
| `lookup` | `{ table, key, default? }`: an exact key in a mapping table |
| `map`, `filter` | `{ over, emit }` / `{ over, where }`: each element becomes the current item |
| `join`, `split` | `{ items, separator? }` / `{ text, separator }` (exact separator, trimmed parts) |
| `object`, `array` | Build an object or a list from expressions |
| `if` | `{ cond, then, else? }` |
| `equals`, `startsWith`, `exists`, `not`, `all`, `any` | Conditions (`startsWith` is an exact prefix) |
| `int`, `text`, `lower`, `upper`, `count` | Conversions |

**Bounds** (each refused with its own code and tested in `DeclarativeTransformTests`): a step budget of 4,000,000 per run in which **every value produced pays for its whole size** (one step per node and one per 16 characters: a copy, a literal, a lookup result, a template, `lower`/`upper`/`text`, `join` and `split`), so the budget also bounds memory; 5 seconds per run (the clock is also read after any large charge); nested loops at most 100,000 iterations in product (a list read straight from the input is walked where it is, not copied); expressions at most 64 levels deep; output and copied data at most 128 levels deep (`transform.too-deep`); mapping tables of at most 10,000 entries in all; a transform document of at most 1 MB; output of at most 5 MB, measured while it is written and stopped at the limit; and **no regular expressions**. `split` trims its parts and drops empty ones. Transforms are fuzzed: any transform on any input ends in a value or a `transform.*` refusal.

## Library backups

A full library backup keeps each installed extension's file (`extensions/<sha256>.zip`) and **not its grants**. Such a backup is written as package format **v9** (provisional, ROADMAP "Package format numbers"); a backup with no extension stays v7. A restore installs nothing by itself: each file goes through the full install checks and comes back **turned off, with no permission granted**, and only when no extension with its id is installed; the preview lists it. So a backup someone hands you cannot run their extensions.

## Storage

Database migration **v9** adds the `extensions` table (id, the file's SHA-256, and the manifest, grants and on/off state as JSON). Forward-only, with the usual copy of the v8 database first.

## Tests and evidence

- `DeclarativeTransformTests`: every operator, every refusal, the input reader's bounds, and 2,000 fuzzed transforms.
- `ExtensionTests`: the sample installs from `examples/extensions` after review (and matches its schemas); manifests this build cannot honour; the path allowlist and limits; packages that carry an extension; an import run's preview, confirmation, one-use token and import-derived source; grants bound to the file; permissions, on/off and remove; an export run in a data folder named after a sentinel user with a linked PDF under it and a gap note, whose output never contains either, and transforms that write a path in any spelling refused; the share and personal filters and the sheet export schema; a library backup keeping the extension without grants and a clean-folder restore bringing it back turned off; the dispatcher commands.
- e2e: "installs the sample extension after granting its permissions, runs its import and export hooks, and removes it".
- **Dual review (2026-09-29, both reviewers and the cross-check; none refuted).** Fixed: fuel did not bound memory (a copy paid for its top level only, literals and lookups for one step, and the output was fully built before its size check); a previewed run survived a withdrawn permission; reinstalling did not repair a damaged stored file; a restored extension could never be granted (`extension.review` added); the user name was matched inside ordinary words; deep output surfaced as an internal error; a hook without a kind read as import and unknown hook fields were ignored; the UI could not pick sources for `read.content`; cancelling the Save dialog lost the output; a dropped caster's spells were not counted; an invalid hit die could break the schema; restore IO errors failed a committed restore; a one-column CSV lost empty rows; the scan test proved only the user-name match.
- **Evidence level: fixture-verified, not Windows-install verified.** Not verified: the native Open and Save dialogs for extensions (DevHost uses the browser picker and a download), and a Narrator pass (accessibility item 26).
