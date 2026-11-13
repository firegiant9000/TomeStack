# Down-conversion goldens ("Save for an older TomeStack", D85)

Everything here is original test data written for TomeStack: no SRD or third-party rules text, no book or setting names.

`frozen/` holds outputs 0.8.1 made (for 0.7.0); they are never regenerated, and they guard the files users already made (`FrozenDownConversionImportTests`).

Each folder pins what a character export made for an older release writes, byte for byte
(`DownConversionGoldenTests.Golden_copy_matches_byte_for_byte`).

| Folder | Holds |
|---|---|
| `for-0.8.0/library/` | "Compat Hero" at character schema v11 (the 0.8.0 maximum, so no character step runs) with the held "Compat Test Blade" (equipped, with a mod; kept: 0.8.0 reads content v12), a v13 pack item "Compat Kit Satchel" (not equipped) with `contains` (two "Compat Spare Rope", the item inside it), the "Surge" toggle with roundsLeft 3, the character's details (a backstory), a v13 creation guide "Compat Kit Guide" for the class, the class "Compat Path of Kits" (v13) whose subclass "Compat Kit Walker" (v13) grants the v13 feature "Compat Kit Mark" (cascade copies, lowered to v12), the v12 feature "Compat Steady Hands" (passes through) and the bundled revision `51e00000-0000-4000-8000-000000000001` (in the 0.8.0 set; written as it is), all under the source "Compat Homebrew A" (`pack.json`). A 0.8.0 copy of the character loses only the satchel's `contains` list (`below.item-contains`; the item stays); the guide is left out whole (`below.creation-guide`) by the whole-source export only, since no character pins a guide. Nothing blocks, and `below.gear-as-custom-item` is never written. |
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

| File (in `for-0.8.0/`) | What it is |
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
- A library backup writes every source in the store, including the bundled SRD sources and their notices. The test takes out
  every source other than the fixture's with its content, then fixes the manifest's `entries`, `notices` and `revisionOrder`, and
  checks that the result restores with the current build to the same sheet.

`expected.json` is computed by importing the written character share into a fresh store with the current build and reading
the character's sheet (`character.get`, the field names the harness emits: `fields[<field>] = value`). It lists the fields
the lowered copy keeps (ability scores, a modifier, proficiency bonus, a save, a skill, passive Perception, speed, initiative,
armor class, hit points), none of which comes from a dropped effect. It sits beside `expected/` (entry goldens); they are separate.
Two fields depend on lowered content, so an old build that mis-reads a lowered copy fails here: `armorClass` 13 (10 + Dex 2 +
an always-on bonus of 1 added at test time to a lowered revision, "Compat Kit Mark") and `hitPoints` 9 (a level-1 class with a d8 hit die, "Compat Path of Kits", a cascade copy). If lowering dropped either effect, those would read 12 and 0.

The owner's first `scripts/old-release-check.ps1` run is the real validation of the library backup, which is post-processed
(above) and was only checked against the current build.

The run writes one outcome per release; commit it as `outcomes/<T>.json`. `OldReleaseOutcomeTests` fails while a `for-<T>/`
with packages has no outcome, or when an outcome no longer hashes to the committed packages and `expected.json`, so rerun and
recommit after regenerating. From the repository root:

```
./scripts/old-release-check.ps1 -Tags v0.8.0 -Inputs 'tests/RulesFixtures/downconvert/for-{target}' -Expected 'tests/RulesFixtures/downconvert/for-{target}' -Out 'tests/RulesFixtures/downconvert/outcomes/{target}.json' -Work "$env:TEMP/old-release-work"
```

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
