# Writing an extension

For people who want to add an import or an export to TomeStack. An extension is **data, not code**: a manifest and one transform document per hook, which TomeStack's own interpreter reads within strict limits ([ADR-011](../decisions/ADR-011-extension-api.md), option A). Nothing you write can reach the network, the file system or anything but the one input a user gives it.

This guide builds a small export, **Example feature list**, which writes a character's features as a Markdown file, followed by the license notices of the sources they come from. Its two JSON blocks are the whole extension: the tests (`AuthoringGuideTests`, and the e2e flow "follows the authoring guides") zip them exactly as below, install them, grant them and run them. A larger sample, with an import hook, is in [examples/extensions/spell-list-and-sheet-summary](../../examples/extensions/spell-list-and-sheet-summary/).

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
  "description": "Writes a character's features as a Markdown list, with every license notice. (Authoring guide example.)",
  "extensionApi": 1,
  "runtime": "declarative",
  "permissions": ["read.sheet", "export.file"],
  "hooks": [
    {
      "kind": "export",
      "id": "features-md",
      "label": "Export features as Markdown",
      "produces": "text",
      "fileExtension": ".md",
      "transform": "transforms/features-md.json"
    }
  ]
}
```

- `id` is a UUID you make once and keep: it is how TomeStack knows an update is the same extension. The name is never identity. **Do not copy the id above:** make your own, for example with `[guid]::NewGuid()` in PowerShell. An extension with the same id as one already installed is treated as an update of it.
- `license` is required. Put your own name in `author`, and keep it: it must stay the same across updates, or the update is refused.
- `permissions` asks for only what the hooks need. The user ticks each one at install; a hook whose permissions are not granted cannot run. The five permissions are `read.sheet`, `read.content`, `import.file`, `write.drafts` and `export.file` ([features/extensions.md](../features/extensions.md#permissions)).
- Each hook is `import` (it reads one JSON or CSV file the user picks, `accepts`) or `export` (it writes one file, `produces` text or JSON). An import hook needs `import.file` and `write.drafts`; an export hook needs `export.file` and `read.sheet` or `read.content`.
- Any field TomeStack does not know refuses the install, so a newer extension never half-works on an older build.

## 2. The transform: transforms/&lt;hook id&gt;.json

A transform turns its input into its output. An export's input is `{ "sheet": <the sheet export model> }` (with `read.sheet`) and/or `{ "content": [ published revisions ] }` (with `read.content`). An import's input is the parsed file; a CSV becomes `{ "rows": [ { column: value } ] }`, and the output is a list of drafts (`kind`, `name`, `summary`, `effects`).

**An export must carry the license notices.** The sheet's `notices` list names every source the file draws on, with its license and attribution. Write each one's title and license into your output (the last lines below do); TomeStack refuses an export that leaves one out (`extension.notices-missing`).

```json tomestack-example:transforms/features-md.json
{
  "description": "One Markdown line per feature, then every license notice.",
  "output": {
    "join": {
      "separator": "\n",
      "items": {
        "array": [
          { "template": "# Features of {/sheet/character/name}" },
          "",
          {
            "join": {
              "separator": "\n",
              "items": {
                "map": {
                  "over": { "get": "/sheet/features" },
                  "emit": { "template": "- {/name} ({/kind}), from {/source}" }
                }
              }
            }
          },
          "",
          "## Sources and licenses",
          {
            "join": {
              "separator": "\n",
              "items": {
                "map": {
                  "over": { "get": "/sheet/notices" },
                  "emit": { "template": "- {/title} ({/publisher}), {/license}. {/attribution} {/modificationNotice}" }
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

The language, in short: a string, number, true, false or null is itself; a list is written `{ "array": [ … ] }`, an object `{ "object": { … } }`, and any other JSON as is `{ "const": … }`; every other object has exactly one operator and computes a value. `get` and `getRoot` read the current item or the whole input with a JSON Pointer; `template` fills `{/pointer}` placeholders; `map` and `filter` walk a list; `join`, `split`, `lookup` (in your `tables`), `if`, `equals`, `startsWith`, `exists`, `not`, `all`, `any`, `int`, `text`, `lower`, `upper` and `count` do what they say. There are no regular expressions, no escaping and no way to call anything: `template` inserts values exactly as they are, so a format that needs escaping (CSV quotes, for example) cannot be written safely from names you do not control. Prefer plain text, Markdown or JSON output. The full reference and the limits are in [features/extensions.md](../features/extensions.md#the-transform-language) and [the schema](../schemas/extension-transform.v1.schema.json).

**Limits you will meet:** a run has a budget of steps in which everything it copies or builds pays for its size, 5 seconds, loops inside loops at most 100,000 times in all, 5 MB of output, and data at most 128 levels deep. A transform that reaches a limit is refused with the limit's name (`transform.fuel`, `transform.timeout`, `transform.iterations`, `transform.output-too-large`, `transform.too-deep`). Read lists where they are (`"over": { "get": … }`) rather than copying them first.

## 3. Package it

An extension file is a ZIP holding `extension.json` and `transforms/` at its root, and nothing else, named `*.tomestack-ext.zip`. In a terminal, from the folder that holds them (`tar` comes with Windows 10 and 11):

```powershell
tar -a -c -f example-feature-list.tomestack-ext.zip extension.json transforms
```

`Compress-Archive` works too: Windows PowerShell 5.1 writes `transforms\features-md.json` with a backslash, which TomeStack reads as `transforms/features-md.json`.

## 4. Install, grant and run

On the **Extensions** screen choose **Install extension…** and pick the file. The review shows what it is, each permission in words (unticked until you tick it) and what it can do. Tick the permissions and choose **Install with these permissions**. Then choose the hook (**Export features as Markdown**), pick a character and what the file may hold, choose **Preview output**, and **Save output…**.

What a user can count on, whatever an extension does:

- Every run is started by them and previews its result before anything is written.
- An export leaves out content that may not be shared (its totals stay). It is refused if it leaves out a license notice, or if it contains their data-folder, profile or linked-PDF path, or their Windows user name as part of a path. (Other text is the extension's to write: TomeStack cannot tell a folder path in a feature name from any other text.)
- An import writes drafts only, into a new source that is never shared, and nothing is active until they publish it.
- Removing the extension revokes everything; drafts it made stay.

## When something is refused

The install review says what is wrong in words. It stops at the first kind of problem it finds (a file that should not be there, then the manifest, then the transforms), so fix that and try again. Behind each message is a code, for example `extension.permission-unknown`, `extension.api-unsupported`, `extension.license-required`, `extension.entry-not-allowed` (a file other than the manifest and transforms), `extension.hook-permission` (a hook needs a permission the manifest does not ask for) or `transform.invalid` (with what is wrong); the tests and the service use the codes. A run that fails names the hook and the limit or problem, never the user's data.
