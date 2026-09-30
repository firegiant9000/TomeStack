# Writing an extension

For people who want to add an import or an export to TomeStack. An extension is **data, not code**: a manifest and one transform document per hook, which TomeStack's own interpreter reads within strict limits ([ADR-011](../decisions/ADR-011-extension-api.md), option A). Nothing you write can reach the network, the file system or anything but the one input a user gives it.

This guide builds a small export, **Example feature list**, which writes a character's features as a CSV file. Its two JSON blocks are the whole extension: the tests (`AuthoringGuideTests`, and the e2e flow "follows the authoring guides") zip them exactly as below, install them, grant them and run them. A larger sample, with an import hook, is in [examples/extensions/spell-list-and-sheet-summary](../../examples/extensions/spell-list-and-sheet-summary/).

## Compatibility

| Extension API | TomeStack | Runtime | Data it reads |
| --- | --- | --- | --- |
| 1 | M6 and later (`app.info.extensionApi` lists what a build runs) | `declarative` | sheet export model v1 (`docs/schemas/sheet-export.v1.schema.json`); content revisions up to content schema v9 |

A newer API is a new revision of ADR-011 and a new row here. An extension written for an API a build does not run is refused, never partly loaded.

## 1. The manifest: extension.json

```json tomestack-example:extension.json
{
  "format": "tomestack.extension",
  "formatVersion": 1,
  "id": "6f5a0000-0000-4000-8000-00000000a001",
  "name": "Example feature list",
  "version": "1.0.0",
  "author": "TomeStack guide",
  "license": "Apache-2.0",
  "description": "Writes a character's features as CSV: name, kind, source. (Authoring guide example.)",
  "extensionApi": 1,
  "runtime": "declarative",
  "permissions": ["read.sheet", "export.file"],
  "hooks": [
    {
      "kind": "export",
      "id": "features-csv",
      "label": "Export features as CSV",
      "produces": "text",
      "fileExtension": ".csv",
      "transform": "transforms/features-csv.json"
    }
  ]
}
```

- `id` is a UUID you make once and keep: it is how TomeStack knows an update is the same extension. The name is never identity.
- `license` is required. `author` must stay the same across updates, or the update is refused.
- `permissions` asks for only what the hooks need. The user ticks each one at install; a hook whose permissions are not granted cannot run. The five permissions are `read.sheet`, `read.content`, `import.file`, `write.drafts` and `export.file` ([features/extensions.md](../features/extensions.md#permissions)).
- Each hook is `import` (it reads one JSON or CSV file the user picks, `accepts`) or `export` (it writes one file, `produces` text or JSON). An import hook needs `import.file` and `write.drafts`; an export hook needs `export.file` and `read.sheet` or `read.content`.
- Any field TomeStack does not know refuses the install, so a newer extension never half-works on an older build.

## 2. The transform: transforms/&lt;hook id&gt;.json

A transform turns its input into its output. An export's input is `{ "sheet": <the sheet export model> }` (with `read.sheet`) and/or `{ "content": [ published revisions ] }` (with `read.content`). An import's input is the parsed file; a CSV becomes `{ "rows": [ { column: value } ] }`, and the output is a list of drafts (`kind`, `name`, `summary`, `effects`).

```json tomestack-example:transforms/features-csv.json
{
  "description": "One CSV line per feature, after a header. Quotes in names are doubled.",
  "output": {
    "join": {
      "separator": "\n",
      "items": {
        "array": [
          "name,kind,source",
          {
            "join": {
              "separator": "\n",
              "items": {
                "map": {
                  "over": { "get": "/sheet/features" },
                  "emit": { "template": "\"{/name}\",{/kind},\"{/source}\"" }
                }
              }
            }
          },
          ""
        ]
      }
    }
  }
}
```

The language, in short: a JSON literal is itself, and an object with one operator computes a value. `get` and `getRoot` read the current item or the whole input with a JSON Pointer; `template` fills `{/pointer}` placeholders; `map` and `filter` walk a list; `join`, `split`, `lookup` (in your `tables`), `if`, `equals`, `startsWith`, `exists`, `not`, `all`, `any`, `int`, `text`, `lower`, `upper` and `count` do what they say. There are no regular expressions and no way to call anything. The full reference and the limits are in [features/extensions.md](../features/extensions.md#the-transform-language) and [the schema](../schemas/extension-transform.v1.schema.json).

**Limits you will meet:** a run has a budget of steps in which everything it copies or builds pays for its size, 5 seconds, loops inside loops at most 100,000 times in all, 5 MB of output, and data at most 128 levels deep. A transform that reaches a limit is refused with the limit's name (`transform.fuel`, `transform.timeout`, `transform.iterations`, `transform.output-too-large`, `transform.too-deep`). Read lists where they are (`"over": { "get": … }`) rather than copying them first.

## 3. Package it

An extension file is a ZIP holding `extension.json` and `transforms/` at its root, and nothing else, named `*.tomestack-ext.zip`. In PowerShell, from the folder that holds them:

```powershell
Compress-Archive -Path extension.json, transforms -DestinationPath example-feature-list.tomestack-ext.zip
```

## 4. Install, grant and run

On the **Extensions** screen choose **Install extension…** and pick the file. The review shows what it is, each permission in words (unticked until you tick it) and what it can do. Tick the permissions and choose **Install with these permissions**. Then choose the hook (**Export features as CSV**), pick a character and what the file may hold, choose **Preview output**, and **Save output…**.

What a user can count on, whatever an extension does:

- Every run is started by them and previews its result before anything is written.
- An export leaves out content that may not be shared (its totals stay), carries every license notice the sheet has, and is refused if it would contain a folder path or their user name.
- An import writes drafts only, into a new source that is never shared, and nothing is active until they publish it.
- Removing the extension revokes everything; drafts it made stay.

## When something is refused

The install review lists every problem with its code, for example `extension.permission-unknown`, `extension.api-unsupported`, `extension.license-required`, `extension.entry-not-allowed` (a file other than the manifest and transforms), `extension.hook-permission` (a hook needs a permission the manifest does not ask for) or `transform.invalid` (with what is wrong). A run that fails names the hook and the limit or problem, never the user's data.
