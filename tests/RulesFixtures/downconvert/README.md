# Down-conversion goldens ("Save for an older TomeStack", D85)

Everything here is original test data written for TomeStack: no SRD or third-party rules text, no book or setting names.

Each folder pins what a character export made for an older release writes, byte for byte
(`DownConversionGoldenTests.Golden_copy_matches_byte_for_byte`).

| Folder | Holds |
|---|---|
| `for-0.6.0/library/` | "Compat Hero" at character schema v10 with an active toggle that has rounds left, pinning the v9 feature "Compat Steady Hands" (passes through), v11 features that cover every warn row of spec section 2 "Content v11 → v10" that can warn without a counting-up pool (Tide Stance, Calm Waters, Rising Tide, Deep Currents), the class "Compat Path of Tides" whose subclass "Compat Tide Walker" grants the v11 feature "Compat Tide Mark" (two cascade copies), and the bundled revision `51e00000-0000-4000-8000-000000000001` (in the 0.6.0 set; written as it is), all under the source "Compat Homebrew A" (`pack.json`). A 0.6.0 copy drops `roundsLeft` and the listed effects and fields; nothing blocks. "Compat Draw Down" has a roll with `drawsDown: true` on a counting-up pool declared in another content ("Compat Reservoir", not pinned, so its own block does not apply). `resource.counts` and `resourceFull` exist only on a counting-up pool, which blocks, so they are not here. |
| `for-0.5.0/library/` | "Compat Hero" at character schema v9 with details, a custom item and a fire resistance, pinning v10 revisions with resistance and immunity grants, item charges, a speed bonus and a passive bonus, a v11 revision that goes straight to v9, and the same bundled revision (in the 0.5.0 set). A 0.5.0 copy drops all of these; nothing blocks. |
| `<folder>/expected/entries/` | Every entry of the package the export writes, under its path in the zip, except the bundled SRD revision and its source: their text lives only in `src/AppService/Content/`, so the test compares those two entries with the store and the manifest's matching `sha256`, `size` and notice text appear as `{bundled}`. |
| `<folder>/expected/changes.json` | The `changes` list the preview returns (what the copy loses). |

The test loads the library into a fresh store, previews the share export for the target, exports with the preview's
`changesHash`, compares each entry with `expected/entries/`, validates the manifest against
`docs/schemas/package-manifest.v<format>.schema.json` and compares the change list with `changes.json`.

The manifest's `appVersion` is the true maker and changes every release, so both sides write it as `{appVersion}`
before comparing, and line endings are compared as LF (the repository stores LF; the writer uses the platform's). The
manifest's `sha256` and `size` values are taken from the bytes the writer produced on Windows (CRLF), so the golden is a
Windows test, like the app. Everything else is exact.

## Harness inputs (lane E, "real older build")

`for-<T>/` holds, beside `library/` and `expected/` (entry goldens), the files lane E opens with the real release T
(`scripts/old-release-check.ps1`; the owner runs it). All are original test data; every fixture name starts with "Compat".

| File (in `for-0.6.0/`, and the same with `0.5.0` in `for-0.5.0/`) | What it is |
|---|---|
| `Compat-Hero-for-<T>.tomestack.zip` | character share |
| `Compat-Hero-for-<T>-personal-backup.tomestack.zip` | character backup |
| `Compat-Homebrew-A-source-pack-for-<T>.tomestack.zip` | source pack |
| `Compat-Campaign-campaign-pack-for-<T>.tomestack.zip` | campaign pack (allows "Compat Homebrew A") |
| `library-backup-for-<T>.tomestack.zip` | full library backup (the harness restores files named `library-backup*`) |
| `expected.json` | `{ "characters": { "<id>": { "fields": { ... } } } }`, the sheet the old build must calculate |

How they are made (`DownConversionGoldenTests.Harness_inputs_match_the_committed_files`): the test loads `library/` into a
fresh store with the clock pinned to `TempApp.Now`, marks the source shareable, saves a campaign with a fixed id, and exports
each package for target T through the same commands the app uses (preview, then export with the previewed `changesHash`).
Three test-time choices keep the committed zips free of SRD text (CONTRIBUTING invariant; ruling R-C17), none of which edits
`library/`:

- The character's bundled SRD pin (`51e00000-...-0001`) is dropped from the copy that is exported, so no bundled revision or
  source goes into a zip. The bundled-pin case stays covered by `expected/` (which compares it with the store).
- "Compat Reservoir" and "Compat Draw Down" (0.6.0 only) are left out: a counting-up pool blocks a whole-source export by design.
- A library backup writes every source in the store, including the bundled SRD sources and their notices. The test takes out
  every source other than the fixture's with its content, then fixes the manifest's `entries`, `notices` and `revisionOrder`, and
  checks that the result restores with the current build to the same sheet.

`expected.json` is computed by importing the written character share into a fresh store with the current build and reading
the character's sheet (`character.get`, the field names the harness emits: `fields[<field>] = value`). It lists the fields
the lowered copy keeps (ability scores, a modifier, proficiency bonus, a save, a skill, passive Perception, speed, initiative,
armor class, hit points), none of which comes from a dropped effect. It sits beside `expected/` (entry goldens); they are separate.

The test produces every zip again and compares entry by entry (names and bytes; the manifest's `appVersion` is neutral, so a
release bump does not stale the files), and compares `expected.json` as text. A stale input fails. Entry bytes are as the writer
makes them on Windows (CRLF), like the entry goldens. Regenerating also rewrites these files.

## Regenerating `expected/` and the harness inputs

Only when the lowering, the package format or a fixture changes on purpose. From the repository root:

```
$env:TOMESTACK_REGENERATE_GOLDENS = "1"
dotnet test tests/AppService.Tests -c Release --filter FullyQualifiedName~DownConversionGoldenTests
Remove-Item Env:TOMESTACK_REGENERATE_GOLDENS
```

That rewrites both `expected/` folders and the harness inputs from the implementation. The regenerate run fails on purpose, so it never reads as green; that failure is expected, not a bug. Then review the diff by eye against SPEC section 2 (what a
copy for each release keeps, drops and blocks) before committing: the golden proves the output is stable, not that it is right.
